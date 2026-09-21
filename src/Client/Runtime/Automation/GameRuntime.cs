using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Void.Client.Failures;
using Void.Client.Models;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private async Task<MinecraftWindowLease> AcquirePreparedWindowLeaseAsync(string display, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string windowId = await WaitForLargestWindowAsync(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            MinecraftWindowLease lease = new(windowId, Interlocked.Increment(ref _nextWindowGeneration));

            try
            {
                await ResizeWindowToDisplayAsync(windowId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                await RunOrThrow(cancellationToken, command: ["xdotool", "windowfocus", windowId]).ConfigureAwait(continueOnCapturedContext: false);

                return lease;
            }
            catch (Exception exception) when (exception is ExternalProcessException or InvalidOperationException)
            {
                var staleWindowFailure = await GetStaleWindowFailureAsync(lease, display, exception, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                if (staleWindowFailure is not null)
                {
                    await Console.Error.WriteLineAsync($"{staleWindowFailure.Message} while preparing the window; reacquiring").ConfigureAwait(continueOnCapturedContext: false);

                    continue;
                }

                throw new GameClientException(
                    code: "client.window.prepare.failed",
                    operation: "window",
                    stage: "prepare",
                    $"Preparing Minecraft window {windowId} failed: {exception.Message}",
                    exception
                );
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task AttachAgentAsync(RunningGame game, CancellationToken cancellationToken)
    {
        int javaProcessId = FindJavaProcessId(game.Process.Id) ?? throw new InvalidOperationException(message: "The running Minecraft JVM could not be found");
        string agentPathAndArguments = $"{PortableMinecraftAgentPath}={CreateAgentArguments(game.Tracker)}";

        var result = await RunProcessTextAsync(
            CreateProcessStartInformation(
                PortableMinecraftJvmAttachPath,
                [javaProcessId.ToString(CultureInfo.InvariantCulture), "load", "instrument", "false", agentPathAndArguments]
            ),
            TimeSpan.FromSeconds(seconds: 10),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (result.ExitCode is not 0)
            throw new InvalidOperationException($"The Minecraft agent could not attach: {result.StandardError}");
    }

    private async Task<byte[]> CaptureScreenAsync(CancellationToken cancellationToken)
    {
        string display = Environment.GetEnvironmentVariable(variable: "DISPLAY") ?? DefaultDisplay;
        string windowId = await FindLargestWindow(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) ?? throw new InvalidOperationException(message: "no visible window found");
        await ResizeWindowToDisplayAsync(windowId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var captureResult = await RunScreenCaptureBytesAsync(
            currentWindowId => CreateProcessStartInformation(fileName: "import", ["-window", currentWindowId, "png:-"], display),
            windowId,
            display,
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        return captureResult.ExitCode is not 0
            ? throw new InvalidOperationException($"screen capture failed: {captureResult.StandardError}")
            : captureResult.StandardOutput;
    }

    private async Task ConnectThroughAgentAsync(RunningGame game, string serverAddress, CancellationToken cancellationToken)
    {
        var response = await SendAgentCommandAsync(game, command: "connect", serverAddress, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (response.Status is not "ok")
        {
            throw new GameClientException(
                response.Stage is "connection.rejected" ? "client.connect.rejected" : "client.connect.failed",
                operation: "connect",
                response.Stage ?? "agent.connect",
                response.Message ?? "The Minecraft agent returned no diagnostic"
            );
        }

        if (!string.Equals(response.Value, serverAddress, StringComparison.Ordinal))
        {
            throw new GameClientException(
                code: "client.connect.failed",
                operation: "connect",
                stage: "address.verify",
                $"Minecraft agent confirmed {JsonSerializer.Serialize(response.Value)} instead of {JsonSerializer.Serialize(serverAddress)}"
            );
        }

        await Console.Error.WriteLineAsync($"Minecraft agent navigated to and joined {JsonSerializer.Serialize(serverAddress)} through discovered UI actions").ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task DrainOutputAsync(int processId)
    {
        if (_outputTasks.TryGetValue(processId, out var output))
        {
            await output.ConfigureAwait(continueOnCapturedContext: false);

            if (_outputTasks.TryRemove(processId, out var removedOutput))
                GC.KeepAlive(removedOutput);
        }
    }

    private async Task EnsureDisplay(CancellationToken cancellationToken = default)
    {
        string display = Environment.GetEnvironmentVariable(variable: "DISPLAY") ?? DefaultDisplay;
        Environment.SetEnvironmentVariable(variable: "DISPLAY", display);
        await WaitForDisplayReadyAsync(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task<string?> FindLargestWindow(string display, CancellationToken cancellationToken = default)
    {
        var searchProcessStartInformation = CreateProcessStartInformation(fileName: "xdotool", ["search", "--onlyvisible", "--name", ".*"], display);
        var searchResult = await RunProcessTextAsync(searchProcessStartInformation, TimeSpan.FromMilliseconds(ExternalProcessTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (searchResult.ExitCode != 0)
            return null;

        string? largestWindowId = null;
        long largestArea = 0;

        foreach (string candidateWindowId in searchResult.StandardOutput.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmedCandidateId = candidateWindowId.Trim();
            var nameProcessStartInformation = CreateProcessStartInformation(fileName: "xdotool", ["getwindowname", trimmedCandidateId], display);
            var nameResult = await RunProcessTextAsync(nameProcessStartInformation, TimeSpan.FromMilliseconds(ExternalProcessTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            bool windowNameAccepted = nameResult.ExitCode is 0
                                     && !string.IsNullOrWhiteSpace(nameResult.StandardOutput)
                                     && nameResult.StandardOutput.Trim() != LauncherSplashWindowTitle;

            if (!windowNameAccepted)
                continue;

            var geometryProcessStartInformation = CreateProcessStartInformation(fileName: "xdotool", ["getwindowgeometry", "--shell", trimmedCandidateId], display);
            var geometryResult = await RunProcessTextAsync(geometryProcessStartInformation, TimeSpan.FromMilliseconds(ExternalProcessTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (geometryResult.ExitCode != 0)
                continue;

            int width = 0, height = 0;

            foreach (string line in geometryResult.StandardOutput.Split(separator: '\n'))
            {
                if (line.StartsWith(value: "WIDTH=", StringComparison.Ordinal) && int.TryParse(line["WIDTH=".Length..], out int widthValue))
                    width = widthValue;
                else if (line.StartsWith(value: "HEIGHT=", StringComparison.Ordinal) && int.TryParse(line["HEIGHT=".Length..], out int heightValue))
                    height = heightValue;
            }

            if ((long)width * height > largestArea)
            {
                largestArea = (long)width * height;
                largestWindowId = trimmedCandidateId;
            }
        }

        return largestWindowId;
    }

    private async Task<StaleMinecraftWindowException?> GetStaleWindowFailureAsync(MinecraftWindowLease lease, string display, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is ExternalProcessException processException && X11FailureClassifier.IsExplicitStaleWindow(processException))
            return new(lease, exception);

        try
        {
            var processStartInformation = CreateProcessStartInformation(fileName: "xdotool", ["getwindowgeometry", "--shell", lease.Id], display);
            var result = await RunProcessTextAsync(processStartInformation, TimeSpan.FromMilliseconds(ExternalProcessTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return result.ExitCode is 0 ? null : new(lease, exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception probeException) when (probeException is ExternalProcessException or InvalidOperationException or TimeoutException or IOException)
        {
            await Console.Error.WriteLineAsync($"Stale-window probe failed; preserving the original error: {probeException.Message}").ConfigureAwait(continueOnCapturedContext: false);

            return null;
        }
    }

    private async Task<bool> IsDisplayReadyAsync(string display, CancellationToken cancellationToken)
    {
        try
        {
            var processStartInformation = CreateProcessStartInformation(fileName: "xdpyinfo", ["-display", display], display);
            var result = await RunProcessTextAsync(processStartInformation, TimeSpan.FromMilliseconds(DisplayProbeTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return result.ExitCode == 0;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async Task<RunningGame> LaunchGameAsync(
        string minecraftDirectory,
        string portableMinecraftVersion,
        IReadOnlyList<string> arguments,
        int? memoryMb,
        CancellationToken cancellationToken
    )
    {
        using (await _windowOperations.LockAsync(cancellationToken))
        {
            _windowSessionId = diagnostics?.CurrentSessionId;
            await PrepareDisplayAndWindowAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await Console.Error.WriteLineAsync($"Launching Minecraft with PortableMC version: {portableMinecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);
            var tracker = CreateTrackerConnection(FindUsername(arguments));
            await (diagnostics?.RegisterSecretAsync(tracker.Token, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
            await (diagnostics?.RegisterSecretAsync(EncodeAgentArgument(tracker.Token), cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);

            string?[] launchArguments = memoryMb is { } value
                ? [.. arguments.Append(CreateMaximumHeapArgument(value)).Append($"--jvm-arg=-javaagent:{PortableMinecraftAgentPath}={CreateAgentArguments(tracker)}").Cast<string?>()]
                : [.. arguments.Append($"--jvm-arg=-javaagent:{PortableMinecraftAgentPath}={CreateAgentArguments(tracker)}").Cast<string?>()];

            long? initialOutOfMemoryKillCount = CgroupMemoryEvents.ReadOutOfMemoryKillCount();
            Process process;

            try
            {
                process = LaunchPortableMinecraftClient(minecraftDirectory, portableMinecraftVersion, launchArguments, cancellationToken);
            }
            catch
            {
                File.Delete(tracker.DescriptorPath);

                throw;
            }

            RunningGame? runningGame = null;

            try
            {
                runningGame = new RunningGame(
                    process,
                    memoryMb,
                    initialOutOfMemoryKillCount,
                    DrainOutputAsync(process.Id),
                    portableMinecraftVersion,
                    DateTimeOffset.UtcNow,
                    tracker
                );
                var managedProcess = runningGame.Process;

                using CancellationTokenSource windowCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var windowTask = WaitForPreparedLargestWindowAsync(Environment.GetEnvironmentVariable(variable: "DISPLAY") ?? DefaultDisplay, windowCancellationTokenSource.Token);
                var processExitTask = managedProcess.WaitForExitAsync(CancellationToken.None);

                if (await Task.WhenAny(windowTask, processExitTask).ConfigureAwait(continueOnCapturedContext: false) == processExitTask)
                {
                    await windowCancellationTokenSource.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
                    await ((Task)windowTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                    throw new GameProcessExitException(process.ExitCode, managedProcess.WasOutOfMemoryKilled, memoryMb);
                }

                string preparedWindowLease = await windowTask.ConfigureAwait(continueOnCapturedContext: false);

                var result = runningGame;
                runningGame = null;

                return result;
            }
            catch
            {
                KillProcess(process);
                await WaitForKilledProcessAsync(process).ConfigureAwait(continueOnCapturedContext: false);
                await DrainOutputAsync(process.Id).ConfigureAwait(continueOnCapturedContext: false);

                if (runningGame is null)
                    process.Dispose();

                File.Delete(tracker.DescriptorPath);

                throw;
            }
            finally
            {
                runningGame?.Dispose();
            }
        }
    }

    private async Task<RunningGame> LaunchPortableAsync(string portableMinecraftVersion, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        string minecraftDirectory = GetMinecraftDirectory();
        await PreparePortableMinecraftClientAsync(minecraftDirectory, portableMinecraftVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return await LaunchGameAsync(minecraftDirectory, portableMinecraftVersion, arguments, memoryMb, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private Process LaunchPortableMinecraftClient(string directory, string version, string?[]? portableMinecraftArguments = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        portableMinecraftArguments ??= [];
        string[] requestedPortableMinecraftArguments = [.. portableMinecraftArguments.OfType<string>()];

        var process = StartGameProcess(
            processStartInformation =>
        {
            processStartInformation.ArgumentList.Add(item: "--main-dir");
            processStartInformation.ArgumentList.Add(directory);
            processStartInformation.ArgumentList.Add(item: "start");
            processStartInformation.ArgumentList.Add(version);

            if (!HasPortableMinecraftArgument(requestedPortableMinecraftArguments, argumentName: "--resolution"))
            {
                processStartInformation.ArgumentList.Add(item: "--resolution");
                processStartInformation.ArgumentList.Add(DisplayScreenResolution);
            }

            if (File.Exists(PortableMinecraftLegacyJvmExecutablePath))
            {
                bool isMojangVersion = version.StartsWith(value: "mojang:", StringComparison.Ordinal);
                bool usesLegacyLwjgl = isMojangVersion && UsesLegacyLwjgl(version);
                string armLwjglVersion = isMojangVersion ? GetArmLwjglVersion(version) : PortableMinecraftArmLwjgl4Version;

                if (usesLegacyLwjgl && !HasPortableMinecraftArgument(requestedPortableMinecraftArguments, argumentName: "--jvm"))
                {
                    processStartInformation.ArgumentList.Add(item: "--jvm");
                    processStartInformation.ArgumentList.Add(PortableMinecraftLegacyJvmPath);
                }
                else if (!usesLegacyLwjgl && !HasPortableMinecraftArgument(requestedPortableMinecraftArguments, argumentName: "--fix-lwjgl"))
                {
                    processStartInformation.ArgumentList.Add(item: "--fix-lwjgl");
                    processStartInformation.ArgumentList.Add(armLwjglVersion);

                    if (armLwjglVersion == PortableMinecraftArmLwjgl4Version)
                    {
                        processStartInformation.ArgumentList.Add(item: "--include-class");
                        processStartInformation.ArgumentList.Add(PortableMinecraftArmLwjgl4ClassPath);
                        processStartInformation.ArgumentList.Add(item: "--include-class");
                        processStartInformation.ArgumentList.Add(PortableMinecraftArmLwjgl4NativePath);
                    }
                }

                bool vulkanLibraryRequired = armLwjglVersion == PortableMinecraftArmLwjgl4Version
                                            && !HasPortableMinecraftArgument(requestedPortableMinecraftArguments, argumentName: "--exclude-lib");

                if (vulkanLibraryRequired)
                {
                    processStartInformation.ArgumentList.Add(item: "--exclude-lib");
                    processStartInformation.ArgumentList.Add(PortableMinecraftArmVulkanLibrary);
                }
            }

            foreach (string? argument in requestedPortableMinecraftArguments)
                processStartInformation.ArgumentList.Add(argument);
        }
        );

        return process;
    }

    private async Task PrepareDisplayAndWindowAsync(CancellationToken cancellationToken)
    {
        await EnsureDisplay(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await RunOrThrow(cancellationToken, command: ["xset", "r", "off"]).ConfigureAwait(continueOnCapturedContext: false);

        string display = Environment.GetEnvironmentVariable(variable: "DISPLAY") ?? DefaultDisplay;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds: 10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await FindLargestWindow(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) is null)
                return;

            await Task.Delay(millisecondsDelay: 100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        throw new TimeoutException(message: "A previous Minecraft window did not close before the next launch");
    }

    private async Task PreparePortableMinecraftClientAsync(string minecraftDirectory, string portableMinecraftVersion, CancellationToken cancellationToken)
    {
        string configurationDirectory = Path.Combine(minecraftDirectory, path2: "config");
        string modsDirectory = Path.Combine(minecraftDirectory, path2: "mods");
        string optionsPath = Path.Combine(minecraftDirectory, path2: "options.txt");
        string sodiumOptionsPath = Path.Combine(configurationDirectory, path2: "sodium-options.json");
        string serversPath = Path.Combine(minecraftDirectory, path2: "servers.dat");
        var configurationDirectoryInformation = Directory.CreateDirectory(configurationDirectory);
        var modsDirectoryInformation = Directory.CreateDirectory(modsDirectory);

        if (!File.Exists(optionsPath) || new FileInfo(optionsPath).Length == 0)
            File.Copy(PortableMinecraftOptionsPath, optionsPath, overwrite: true);

        if (!File.Exists(sodiumOptionsPath))
            File.Copy(PortableMinecraftSodiumOptionsPath, sodiumOptionsPath);

        if (!File.Exists(serversPath))
        {
            await File.WriteAllBytesAsync(
                serversPath,
                Convert.FromHexString(
                    s: "0a0000090007736572766572730a0000000101000668696464656e000800026970000a766f69643a32353536350800046e616d65000a566f69642050726f78790000"
                ),
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);
        }

        List<string> portableMinecraftArguments = [portableMinecraftVersion, "--demo", "--main-dir", minecraftDirectory, "--output", "machine"];

        bool legacyJavaRequired = File.Exists(PortableMinecraftLegacyJvmExecutablePath)
                                 && !portableMinecraftVersion.StartsWith(value: "mojang:", StringComparison.Ordinal);

        if (legacyJavaRequired)
        {
            portableMinecraftArguments.AddRange(["--fix-lwjgl", PortableMinecraftArmLwjgl4Version]);
            portableMinecraftArguments.AddRange(["--exclude-lib", PortableMinecraftArmVulkanLibrary]);
            portableMinecraftArguments.AddRange(["--include-class", PortableMinecraftArmLwjgl4ClassPath]);
            portableMinecraftArguments.AddRange(["--include-class", PortableMinecraftArmLwjgl4NativePath]);
        }

        var preparationResult = await RunProcessTextAsync(
            CreateProcessStartInformation(PortableMinecraftDryRunPath, portableMinecraftArguments),
            TimeSpan.FromMinutes(minutes: 5),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (!string.IsNullOrWhiteSpace(preparationResult.StandardError))
            await Console.Error.WriteAsync(preparationResult.StandardError).ConfigureAwait(continueOnCapturedContext: false);

        if (preparationResult.ExitCode != 0)
            throw new InvalidOperationException($"Portable Minecraft preparation exited with code {preparationResult.ExitCode}: {preparationResult.StandardError}");

        string? minecraftVersion = preparationResult.StandardOutput
            .Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd(trimChar: '\r').Split(separator: '\t'))
            .Where(static fields => fields.Length > 1 && fields[0] == "loaded_hierarchy")
            .Select(static fields => fields[^1])
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new InvalidOperationException(message: "Portable Minecraft preparation did not report a Minecraft version");

        await InstallSodiumAsync(modsDirectory, portableMinecraftVersion, minecraftVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ResizeWindowToDisplayAsync(string windowId, CancellationToken cancellationToken = default)
    {
        await RunOrThrow(cancellationToken, command: ["xdotool", "windowmove", "--sync", windowId, "0", "0"]).ConfigureAwait(continueOnCapturedContext: false);
        await RunOrThrow(cancellationToken, command: ["xdotool", "windowsize", "--sync", windowId, DisplayScreenWidth, DisplayScreenHeight]).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task RunOrThrow(CancellationToken cancellationToken, params string[] command)
    {
        var processStartInformation = CreateProcessStartInformation(command[0], command[1..]);
        var result = await RunProcessTextAsync(processStartInformation, TimeSpan.FromMilliseconds(ExternalProcessTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (result.ExitCode is not 0)
            throw new ExternalProcessException(command[0], command[1..], result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private async Task<ProcessTextResult> RunProcessTextAsync(ProcessStartInfo processStartInformation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(processStartInformation)
                            ?? throw new InvalidOperationException($"failed to start {processStartInformation.FileName}");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await WaitForProcessExitAsync(process, processStartInformation.FileName, timeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch
        {
            await IgnoreTaskAsync(standardOutputTask).ConfigureAwait(continueOnCapturedContext: false);
            await IgnoreTaskAsync(standardErrorTask).ConfigureAwait(continueOnCapturedContext: false);

            if (diagnostics?.CurrentSessionId is { } failedSession)
            {
                using CancellationTokenSource diagnosticTimeout = new(TimeSpan.FromSeconds(seconds: 3));

                try
                {
                    if (standardOutputTask.IsCompletedSuccessfully)
                    {
                        await diagnostics.WriteOutputAsync(
                            failedSession,
                            stream: "preparation",
                            await standardOutputTask.ConfigureAwait(continueOnCapturedContext: false),
                            diagnosticTimeout.Token
                        ).ConfigureAwait(continueOnCapturedContext: false);
                    }

                    if (standardErrorTask.IsCompletedSuccessfully)
                    {
                        await diagnostics.WriteOutputAsync(
                            failedSession,
                            stream: "preparation",
                            await standardErrorTask.ConfigureAwait(continueOnCapturedContext: false),
                            diagnosticTimeout.Token
                        ).ConfigureAwait(continueOnCapturedContext: false);
                    }
                }
                catch (OperationCanceledException) when (diagnosticTimeout.IsCancellationRequested)
                {
                    await Console.Error.WriteLineAsync(value: "Timed out collecting preparation output").ConfigureAwait(continueOnCapturedContext: false);
                }
            }

            throw;
        }

        string standardOutput = await standardOutputTask.ConfigureAwait(continueOnCapturedContext: false);
        string standardError = await standardErrorTask.ConfigureAwait(continueOnCapturedContext: false);

        if (diagnostics?.CurrentSessionId is { } sessionId)
        {
            await diagnostics.WriteOutputAsync(sessionId, stream: "preparation", standardOutput, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await diagnostics.WriteOutputAsync(sessionId, stream: "preparation", standardError, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return new ProcessTextResult(process.ExitCode, standardOutput, standardError);
    }

    private async Task<ProcessBytesResult> RunScreenCaptureBytesAsync(
        Func<string, ProcessStartInfo> createProcessStartInformation,
        string windowId,
        string display,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 1; attempt <= ScreenCaptureMaximumAttempts; attempt++)
        {
            try
            {
                return await RunProcessBytesAsync(createProcessStartInformation(windowId), TimeSpan.FromMilliseconds(ScreenCaptureTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (TimeoutException exception) when (attempt < ScreenCaptureMaximumAttempts)
            {
                await Console.Error.WriteLineAsync($"{exception.Message}; reacquiring the Minecraft window before screen capture attempt {attempt + 1}").ConfigureAwait(continueOnCapturedContext: false);
                windowId = await WaitForPreparedLargestWindowAsync(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        throw new InvalidOperationException(message: "Screen capture attempts were exhausted");
    }

    private async Task<TrackerResponse> SendAgentCommandAsync(RunningGame game, string command, string value, CancellationToken cancellationToken)
    {
        if (!File.Exists(game.Tracker.DescriptorPath))
            await AttachAgentAsync(game, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (!File.Exists(game.Tracker.DescriptorPath))
            throw new InvalidOperationException(message: "The Minecraft agent is not ready");

        string descriptor = await File.ReadAllTextAsync(game.Tracker.DescriptorPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (!int.TryParse(descriptor, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException(message: "The Minecraft agent published an invalid endpoint");

        string requestId = Guid.NewGuid().ToString(format: "N", CultureInfo.InvariantCulture);
        string encodedValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd(trimChar: '=').Replace(oldChar: '+', newChar: '-').Replace(oldChar: '/', newChar: '_');

        try
        {
            using TcpClient client = new();

            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync($"{game.Tracker.Token}\t{command}\t{requestId}\t{encodedValue}".AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            string? responseJson = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return string.IsNullOrWhiteSpace(responseJson)
                ? throw new InvalidOperationException(message: "The Minecraft agent returned an empty response")
                : JsonSerializer.Deserialize<TrackerResponse>(responseJson, TrackerJsonOptions)
                   ?? throw new InvalidOperationException(message: "The Minecraft agent returned a malformed response");
        }
        catch (OperationCanceledException)
        {
            await TryCancelAgentCommandAsync(game.Tracker, port, requestId).ConfigureAwait(continueOnCapturedContext: false);

            throw;
        }
    }

    private async Task SendChatThroughAgentAsync(RunningGame game, string message, CancellationToken cancellationToken)
    {
        var response = await SendAgentCommandAsync(game, command: "chat", message, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (response.Status is not "ok")
        {
            throw new GameClientException(
                code: "client.chat.failed",
                operation: "send-chat",
                response.Stage ?? "agent.chat",
                response.Message ?? "The Minecraft agent returned no diagnostic"
            );
        }

        if (!string.Equals(response.Value, message, StringComparison.Ordinal))
        {
            throw new GameClientException(
                code: "client.chat.failed",
                operation: "send-chat",
                stage: "chat.verify",
                $"Minecraft agent confirmed {JsonSerializer.Serialize(response.Value)} instead of {JsonSerializer.Serialize(message)}"
            );
        }

        await Console.Error.WriteLineAsync($"Minecraft agent submitted the exact chat input through Minecraft's UI handler: {JsonSerializer.Serialize(message)}").ConfigureAwait(continueOnCapturedContext: false);
    }

    private Process StartGameProcess(Action<ProcessStartInfo> configure)
    {
        ProcessStartInfo processStartInformation = new(fileName: "setsid")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        processStartInformation.ArgumentList.Add(PortableMinecraftLauncherPath);
        configure(processStartInformation);

        var process = Process.Start(processStartInformation)
                      ?? throw new InvalidOperationException(message: "Failed to start the PortableMC process group");

        var sessionId = diagnostics?.CurrentSessionId;
        _outputTasks[process.Id] = Task.WhenAll(
            PumpOutputAsync(process.StandardOutput, Console.Out, sessionId, stream: "stdout"),
            PumpOutputAsync(process.StandardError, Console.Error, sessionId, stream: "stderr")
        );

        return process;
    }

    private async Task WaitForDisplayReadyAsync(string display, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds: 10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsDisplayReadyAsync(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
                return;

            await Task.Delay(millisecondsDelay: 100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        throw new TimeoutException($"Display {display} did not become ready within 10 seconds");
    }

    private async Task<string> WaitForLargestWindowAsync(string display, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? windowId = await FindLargestWindow(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (windowId is not null)
                return windowId;
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task<string> WaitForPreparedLargestWindowAsync(string display, CancellationToken cancellationToken)
    {
        return (await AcquirePreparedWindowLeaseAsync(display, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Id;
    }

    private record ProcessBytesResult(int ExitCode, byte[] StandardOutput, string StandardError);



    private record ProcessTextResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record TrackerResponse(string? Status, string? Stage, string? Message, string? Value, GamePlayer? Local, RemoteGamePlayer[]? Remote);
}
