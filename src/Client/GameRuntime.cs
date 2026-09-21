using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Nito.AsyncEx;

using File = System.IO.File;

namespace Void.Client;

/// <summary>
/// Owns the Linux, PortableMC, CurseForge, X11, and agent-automation details for one Minecraft game process.
/// Lifecycle coordination lives in <see cref="GameCoordinator"/>; a window gate also protects diagnostic captures during cleanup.
/// </summary>
internal sealed partial class GameRuntime(SessionDiagnostics? diagnostics = null) : IGameRuntime
{
    private const int CurseForgeFilesBatchSize = 50;
    private const string DefaultCurseForgeApiBaseUniformResourceLocator = "https://api.curseforge.com";
    private const string DefaultDisplay = ":99";
    private const string DefaultMinecraftDirectory = "/root/.minecraft";
    private const int DisplayProbeTimeoutMilliseconds = 1000;
    private const string DisplayScreenHeight = "480";
    private const string DisplayScreenResolution = $"{DisplayScreenWidth}x{DisplayScreenHeight}";
    private const string DisplayScreenWidth = "854";
    private const int ExternalProcessTimeoutMilliseconds = 5000;
    private const string LauncherSplashWindowTitle = "Void Client Startup";
    private const int MinecraftGameIdentifier = 432;
    private const int PlayerReadTimeoutMilliseconds = 2000;
    private const string PortableMinecraftAgentPath = "/opt/portableminecraftclient/void-client-agent.jar";
    private const string PortableMinecraftArmLwjgl3Version = "3.3.3";
    private const string PortableMinecraftArmLwjgl4ClassPath = "/opt/portableminecraftclient/lwjgl-3.4.1-unsafe.jar";
    private const string PortableMinecraftArmLwjgl4NativePath = "/opt/portableminecraftclient/lwjgl-3.4.1-natives-linux-arm64.jar";
    private const string PortableMinecraftArmLwjgl4Version = "3.4.1";
    private const string PortableMinecraftArmVulkanLibrary = "org.lwjgl:lwjgl-vulkan:*:natives-linux-arm64";
    private const string PortableMinecraftDryRunPath = "/usr/local/bin/portablemc-dry-run-with-retries";
    private const string PortableMinecraftJvmAttachPath = "/usr/bin/jattach";
    private const string PortableMinecraftLauncherPath = "/usr/local/bin/launch-portableminecraftclient";
    private const string PortableMinecraftLegacyJvmExecutablePath = "/usr/lib/jvm/zulu-8/bin/java";
    private const string PortableMinecraftLegacyJvmPath = "/usr/local/bin/java-arm64-lwjgl2";
    private const string PortableMinecraftOptionsPath = "/opt/portableminecraftclient/options.txt";
    private const string PortableMinecraftSodiumOptionsPath = "/opt/portableminecraftclient/sodium-options.json";
    private const int ProcessStopTimeoutMilliseconds = 10000;
    private const int ScreenCaptureMaximumAttempts = 3;
    private const int ScreenCaptureTimeoutMilliseconds = 10000;
    private static readonly JsonSerializerOptions CurseForgeManifestJavaScriptObjectNotationOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    private static readonly JsonSerializerOptions TrackerJavaScriptObjectNotationOptions = new(JsonSerializerDefaults.Web);
    private readonly AsyncLock _windowOperations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> _outputTasks = new();
    private long _nextWindowGeneration;
    private Guid? _windowSessionIdentifier;

    public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        using (await _windowOperations.LockAsync(cancellationToken))
        {
            return diagnostics?.CurrentSessionIdentifier is { } sessionIdentifier && _windowSessionIdentifier != sessionIdentifier
                ? throw new InvalidOperationException(message: "No matching Minecraft session is available for a screenshot")
                : await CaptureScreenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public Task ConnectAsync(RunningGame game, string host, int port, CancellationToken cancellationToken)
    {
        return ConnectThroughAgentAsync(game, $"{host}:{port}", cancellationToken);
    }

    public async Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileIdentifier, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(variable: "CURSEFORGE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(message: "CURSEFORGE_API_KEY is not set");

        var minecraftDirectory = GetMinecraftDirectory();

        var portableMinecraftVersion = await PrepareCurseForgeAsync(slug, fileIdentifier, apiKey, CreateCurseForgeApiBaseUniformResourceIdentifier(), minecraftDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return await LaunchGameAsync(minecraftDirectory, portableMinecraftVersion, arguments, memoryMb, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public Task<RunningGame> LaunchNeoForgeAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        return LaunchPortableAsync($"neoforge:{version}", arguments, memoryMb, cancellationToken);
    }

    public Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        return LaunchPortableAsync($"mojang:{version}", arguments, memoryMb, cancellationToken);
    }

    public async Task<GamePlayers> ReadPlayersAsync(RunningGame game, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(PlayerReadTimeoutMilliseconds));

        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            if (!File.Exists(game.Tracker.DescriptorPath))
                await AttachAgentAsync(game, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (!File.Exists(game.Tracker.DescriptorPath))
                throw PlayersUnavailable(message: "The Minecraft player tracker is not ready");

            var descriptor = await File.ReadAllTextAsync(game.Tracker.DescriptorPath, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (!int.TryParse(descriptor, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                throw PlayersUnavailable(message: "The Minecraft player tracker published an invalid endpoint");

            using var client = new TcpClient();

            await client.ConnectAsync(IPAddress.Loopback, port, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(game.Tracker.Token.AsMemory(), linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);
            var responseJavaScriptObjectNotation = await reader.ReadLineAsync(linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (string.IsNullOrWhiteSpace(responseJavaScriptObjectNotation))
                throw PlayersUnavailable(message: "The Minecraft player tracker returned an empty response");

            var response = JsonSerializer.Deserialize<TrackerResponse>(responseJavaScriptObjectNotation, TrackerJavaScriptObjectNotationOptions)
                           ?? throw PlayersUnavailable(message: "The Minecraft player tracker returned a malformed response");

            if (response.Status is "notInWorld")
                throw new GamePlayersException(StatusCodes.Status409Conflict, response.Message ?? "The Minecraft client has no current player world");

            if (response.Status is not "ok")
                throw PlayersUnavailable(response.Message ?? "The Minecraft player tracker is unavailable");

            if (response.Local is null || response.Remote is null)
                throw PlayersUnavailable(message: "The Minecraft player tracker returned an incomplete response");

            ValidatePlayer(response.Local);

            foreach (var player in response.Remote)
                ValidatePlayer(player);

            return new(response.Local, response.Remote);
        }
        catch (GamePlayersException)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw PlayersUnavailable(message: "The Minecraft player tracker did not respond in time", stage: "response.timeout");
        }
        catch (Exception exception) when (exception is IOException or SocketException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw PlayersUnavailable($"The Minecraft player tracker could not be read: {exception.Message}", stage: "response.read", exception);
        }
    }

    public Task SendChatAsync(RunningGame game, string message, CancellationToken cancellationToken)
    {
        return SendChatThroughAgentAsync(game, message, cancellationToken);
    }

    public async Task<StopMode> StopAsync(RunningGame? game, CancellationToken cancellationToken)
    {
        using (await _windowOperations.LockAsync(cancellationToken))
        {
            try
            {
                if (game is null)
                    return StopMode.AlreadyStopped;

                var launcherHasExited = game.Process.HasExited;

                var terminateResult = await RunProcessTextAsync(
                    CreateProcessStartInformation(fileName: "kill", ["-TERM", "--", $"-{game.Process.Identifier}"]),
                    TimeSpan.FromSeconds(seconds: 5),
                    cancellationToken
                ).ConfigureAwait(continueOnCapturedContext: false);

                if (terminateResult.ExitCode is not 0)
                {
                    if ((launcherHasExited || game.Process.HasExited) && !IsProcessGroupRunning(game.Process.Identifier))
                    {
                        await game.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        return StopMode.AlreadyStopped;
                    }

                    throw new InvalidOperationException($"kill -TERM failed with code {terminateResult.ExitCode}: {terminateResult.StandardError}");
                }

                var mode = StopMode.Graceful;

                try
                {
                    await WaitForProcessGroupExitAsync(game.Process.Identifier, TimeSpan.FromMilliseconds(ProcessStopTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                }
                catch (TimeoutException)
                {
                    var killResult = await RunProcessTextAsync(
                        CreateProcessStartInformation(fileName: "kill", ["-KILL", "--", $"-{game.Process.Identifier}"]),
                        TimeSpan.FromSeconds(seconds: 5),
                        CancellationToken.None
                    ).ConfigureAwait(continueOnCapturedContext: false);

                    if (killResult.ExitCode is not 0 && IsProcessGroupRunning(game.Process.Identifier))
                        throw new InvalidOperationException($"kill -KILL failed with code {killResult.ExitCode}: {killResult.StandardError}");

                    await WaitForProcessGroupExitAsync(game.Process.Identifier, TimeSpan.FromMilliseconds(ProcessStopTimeoutMilliseconds), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
                    mode = StopMode.Forced;
                }

                await game.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                return mode;
            }
            finally
            {
                try
                {
                    if (game is not null)
                        File.Delete(game.Tracker.DescriptorPath);

                    if (_windowSessionIdentifier is { } sessionIdentifier)
                        await ((diagnostics?.CollectAsync(sessionIdentifier, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false));
                }
                finally
                {
                    _windowSessionIdentifier = null;
                }
            }
        }
    }

    public async Task WriteOptionsAsync(string options, CancellationToken cancellationToken)
    {
        var minecraftDirectory = GetMinecraftDirectory();
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));
        var destinationPath = Path.Combine(minecraftDirectory, path2: "options.txt");
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(temporaryPath, options, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    internal static string CreateMaximumHeapArgument(int memoryMb)
    {
        return $"--jvm-arg=-Xmx{memoryMb.ToString(CultureInfo.InvariantCulture)}M";
    }

    internal async Task PumpOutputAsync(TextReader reader, TextWriter console, Guid? sessionIdentifier, string stream, CancellationToken cancellationToken = default)
    {
        var buffer = new char[4096];
        var pending = "";

        try
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            while (count > 0)
            {
                var text = new string(buffer, startIndex: 0, count);
                await console.WriteAsync(text).ConfigureAwait(continueOnCapturedContext: false);
                pending += text;

                if (sessionIdentifier is { } identifier && diagnostics is not null)
                {
                    // Keep enough overlap to redact raw or encoded agent tokens split across reads.
                    pending = await diagnostics.RedactAsync(identifier, pending, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    var length = Math.Max(val1: 0, pending.Length - 128);
                    await diagnostics.WriteOutputAsync(identifier, stream, pending[..length], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    pending = pending[length..];
                }
                else
                {
                    pending = "";
                }

                count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (sessionIdentifier is { } identifier)
                await ((diagnostics?.WarnAsync(identifier, $"Console collection ended: {exception.Message}", cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false));
        }
        finally
        {
            if (sessionIdentifier is { } identifier)
                await ((diagnostics?.WriteOutputAsync(identifier, stream, pending, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false));
        }
    }

    private static string CreateAgentArguments(GameTrackerConnection tracker)
    {
        var arguments = $"descriptor={EncodeAgentArgument(tracker.DescriptorPath)};token={EncodeAgentArgument(tracker.Token)}";

        return tracker.ExpectedName is null ? arguments : $"{arguments};name={EncodeAgentArgument(tracker.ExpectedName)}";
    }

    static Uri CreateCurseForgeApiBaseUniformResourceIdentifier()
    {
        var configuredBaseUniformResourceLocator = Environment.GetEnvironmentVariable(variable: "CURSEFORGE_API_BASE_URL");

        var baseUniformResourceLocator = string.IsNullOrWhiteSpace(configuredBaseUniformResourceLocator)
            ? DefaultCurseForgeApiBaseUniformResourceLocator
            : configuredBaseUniformResourceLocator.Trim();

        var uniformResourceIdentifierCreated = Uri.TryCreate(baseUniformResourceLocator, UriKind.Absolute, out var baseUniformResourceIdentifier);

        if (!uniformResourceIdentifierCreated || baseUniformResourceIdentifier is null)
            throw new InvalidOperationException(message: "CURSEFORGE_API_BASE_URL must be an absolute HTTP or HTTPS URL");

        var schemeSupported = baseUniformResourceIdentifier.Scheme is "http" or "https";

        if (!schemeSupported)
            throw new InvalidOperationException(message: "CURSEFORGE_API_BASE_URL must use HTTP or HTTPS");

        var path = baseUniformResourceIdentifier.AbsolutePath.TrimEnd(trimChar: '/');

        return new UriBuilder(baseUniformResourceIdentifier)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : path + "/",
            Query = "",
            Fragment = ""
        }.Uri;
    }

    static ProcessStartInfo CreateProcessStartInformation(string fileName, IEnumerable<string> arguments, string? display = null)
    {
        var processStartInformation = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (display is not null)
            processStartInformation.Environment[key: "DISPLAY"] = display;

        foreach (var argument in arguments)
            processStartInformation.ArgumentList.Add(argument);

        return processStartInformation;
    }

    private static GameTrackerConnection CreateTrackerConnection(string? expectedName)
    {
        var descriptorPath = Path.Combine(Path.GetTempPath(), $"void-client-agent-{Guid.NewGuid():N}.port");

        return new GameTrackerConnection(descriptorPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(count: 32)), expectedName);
    }

    static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    static string DetermineTargetDirectory(string fileName, string minecraftDirectory)
    {
        return fileName.EndsWith(value: ".jar", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(minecraftDirectory, path2: "mods")
            : fileName.EndsWith(value: ".zip", StringComparison.OrdinalIgnoreCase)
                ? fileName.Contains(value: "shader", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(minecraftDirectory, path2: "shaderpacks")
                    : Path.Combine(minecraftDirectory, path2: "resourcepacks")
                : Path.Combine(minecraftDirectory, path2: "mods");
    }

    static async Task<byte[]> DownloadWithFallbackAsync(
        HttpClient hypertextTransferProtocolClient,
        string downloadUniformResourceLocator,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUniformResourceLocator);

        if (downloadUniformResourceLocator.Contains(value: "curseforge.com", StringComparison.OrdinalIgnoreCase))
            ReturnedValue.Consume(request.Headers.TryAddWithoutValidation(name: "x-api-key", apiKey));

        using var response = await hypertextTransferProtocolClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var successfulResponse = response.EnsureSuccessStatusCode();

        return await successfulResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static string EncodeAgentArgument(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd(trimChar: '=').Replace(oldChar: '+', newChar: '-').Replace(oldChar: '/', newChar: '_');
    }

    private static int? FindJavaProcessIdentifier(int rootProcessIdentifier)
    {
        var descendants = new HashSet<int> { rootProcessIdentifier };

        var processDirectories = Directory.EnumerateDirectories(path: "/proc")
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Where(item => int.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToArray();

        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var (path, name) in processDirectories)
            {
                var processIdentifier = int.Parse(name, CultureInfo.InvariantCulture);

                var parentProcessFound = TryReadParentProcessIdentifier(path, out var parentProcessIdentifier);
                var processIsNewDescendant = !descendants.Contains(processIdentifier) && parentProcessFound && descendants.Contains(parentProcessIdentifier);

                if (!processIsNewDescendant)
                    continue;

                var descendantAdded = descendants.Add(processIdentifier);
                changed = true;
            }
        }

        foreach (var processIdentifier in descendants.OrderDescending())
        {
            try
            {
                var arguments = File.ReadAllText($"/proc/{processIdentifier}/cmdline").Split(separator: '\0', StringSplitOptions.RemoveEmptyEntries);

                if (arguments.Any(argument => Path.GetFileName(argument) is "java" or "java-x86_64"))
                    return processIdentifier;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Process {processIdentifier} ended during command-line inspection: {exception.Message}");
            }
        }

        return null;
    }

    private static string? FindUsername(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] is "--username" or "-u")
                return index + 1 < arguments.Count ? arguments[index + 1] : null;

            if (arguments[index].StartsWith(value: "--username=", StringComparison.Ordinal))
                return arguments[index]["--username=".Length..];
        }

        return null;
    }

    static string GetArmLwjglVersion(string version)
    {
        var versionComponents = version["mojang:".Length..].Split(separator: '.');

        return versionComponents.Length >= 1 && int.TryParse(versionComponents[0], out var majorVersion) && majorVersion >= 26
            ? PortableMinecraftArmLwjgl4Version
            : PortableMinecraftArmLwjgl3Version;
    }

    private static string GetMinecraftDirectory()
    {
        return Environment.GetEnvironmentVariable(variable: "MINECRAFT_DIRECTORY") ?? DefaultMinecraftDirectory;
    }

    static bool HasPortableMinecraftArgument(IEnumerable<string> arguments, string argumentName)
    {
        return arguments.Any(
            argument => string.Equals(argument, argumentName, StringComparison.Ordinal) || argument.StartsWith($"{argumentName}=", StringComparison.Ordinal)
        );
    }

    static async Task IgnoreTaskAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
