using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Nito.AsyncEx;

using Void.Client.Abstractions;
using Void.Client.Failures;
using Void.Client.Models;
using Void.Client.States;

using File = System.IO.File;

namespace Void.Client;

/// <summary>
/// Owns the Linux, PortableMC, CurseForge, X11, and agent-automation details for one Minecraft game process.
/// Lifecycle coordination lives in <see cref="GameCoordinator"/>; a window gate also protects diagnostic captures during cleanup.
/// </summary>
internal sealed partial class GameRuntime(SessionDiagnostics? diagnostics = null) : IGameRuntime
{
    private const int CurseForgeFilesBatchSize = 50;
    private const string DefaultCurseForgeApiBaseUrl = "https://api.curseforge.com";
    private const string DefaultDisplay = ":99";
    private const string DefaultMinecraftDirectory = "/root/.minecraft";
    private const int DisplayProbeTimeoutMilliseconds = 1000;
    private const string DisplayScreenHeight = "480";
    private const string DisplayScreenResolution = $"{DisplayScreenWidth}x{DisplayScreenHeight}";
    private const string DisplayScreenWidth = "854";
    private const int ExternalProcessTimeoutMilliseconds = 5000;
    private const string LauncherSplashWindowTitle = "Void Client Startup";
    private const int MinecraftGameId = 432;
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
    private static readonly JsonSerializerOptions CurseForgeManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    private static readonly JsonSerializerOptions TrackerJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AsyncLock _windowOperations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> _outputTasks = new();
    private long _nextWindowGeneration;
    private Guid? _windowSessionId;

    public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        using (await _windowOperations.LockAsync(cancellationToken))
        {
            return diagnostics?.CurrentSessionId is { } sessionId && _windowSessionId != sessionId
                ? throw new InvalidOperationException(message: "No matching Minecraft session is available for a screenshot")
                : await CaptureScreenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    public Task ConnectAsync(RunningGame game, string host, int port, CancellationToken cancellationToken)
    {
        return ConnectThroughAgentAsync(game, $"{host}:{port}", cancellationToken);
    }

    public async Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        string? apiKey = Environment.GetEnvironmentVariable(variable: "CURSEFORGE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(message: "CURSEFORGE_API_KEY is not set");

        string minecraftDirectory = GetMinecraftDirectory();

        string portableMinecraftVersion = await PrepareCurseForgeAsync(slug, fileId, apiKey, CreateCurseForgeApiBaseUri(), minecraftDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

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
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMilliseconds(PlayerReadTimeoutMilliseconds));

        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            if (!File.Exists(game.Tracker.DescriptorPath))
                await AttachAgentAsync(game, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (!File.Exists(game.Tracker.DescriptorPath))
                throw PlayersUnavailable(message: "The Minecraft player tracker is not ready");

            string descriptor = await File.ReadAllTextAsync(game.Tracker.DescriptorPath, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (!int.TryParse(descriptor, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
                throw PlayersUnavailable(message: "The Minecraft player tracker published an invalid endpoint");

            using TcpClient client = new();

            await client.ConnectAsync(IPAddress.Loopback, port, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            await writer.WriteLineAsync(game.Tracker.Token.AsMemory(), linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);
            string? responseJson = await reader.ReadLineAsync(linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

            if (string.IsNullOrWhiteSpace(responseJson))
                throw PlayersUnavailable(message: "The Minecraft player tracker returned an empty response");

            var response = JsonSerializer.Deserialize<TrackerResponse>(responseJson, TrackerJsonOptions)
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

                bool launcherHasExited = game.Process.HasExited;

                var terminateResult = await RunProcessTextAsync(
                    CreateProcessStartInformation(fileName: "kill", ["-TERM", "--", $"-{game.Process.Id}"]),
                    TimeSpan.FromSeconds(seconds: 5),
                    cancellationToken
                ).ConfigureAwait(continueOnCapturedContext: false);

                if (terminateResult.ExitCode is not 0)
                {
                    if ((launcherHasExited || game.Process.HasExited) && !IsProcessGroupRunning(game.Process.Id))
                    {
                        await game.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                        return StopMode.AlreadyStopped;
                    }

                    throw new InvalidOperationException($"kill -TERM failed with code {terminateResult.ExitCode}: {terminateResult.StandardError}");
                }

                var mode = StopMode.Graceful;

                try
                {
                    await WaitForProcessGroupExitAsync(game.Process.Id, TimeSpan.FromMilliseconds(ProcessStopTimeoutMilliseconds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                }
                catch (TimeoutException)
                {
                    var killResult = await RunProcessTextAsync(
                        CreateProcessStartInformation(fileName: "kill", ["-KILL", "--", $"-{game.Process.Id}"]),
                        TimeSpan.FromSeconds(seconds: 5),
                        CancellationToken.None
                    ).ConfigureAwait(continueOnCapturedContext: false);

                    if (killResult.ExitCode is not 0 && IsProcessGroupRunning(game.Process.Id))
                        throw new InvalidOperationException($"kill -KILL failed with code {killResult.ExitCode}: {killResult.StandardError}");

                    await WaitForProcessGroupExitAsync(game.Process.Id, TimeSpan.FromMilliseconds(ProcessStopTimeoutMilliseconds), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
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

                    if (_windowSessionId is { } sessionId)
                        await (diagnostics?.CollectAsync(sessionId, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
                }
                finally
                {
                    _windowSessionId = null;
                }
            }
        }
    }

    public async Task WriteOptionsAsync(string options, CancellationToken cancellationToken)
    {
        string minecraftDirectory = GetMinecraftDirectory();
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));
        string destinationPath = Path.Combine(minecraftDirectory, path2: "options.txt");
        string temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

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

    internal async Task PumpOutputAsync(TextReader reader, TextWriter console, Guid? sessionId, string stream, CancellationToken cancellationToken = default)
    {
        char[] buffer = new char[4096];
        string pending = "";

        try
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            while (count > 0)
            {
                string text = new(buffer, startIndex: 0, count);
                await console.WriteAsync(text).ConfigureAwait(continueOnCapturedContext: false);
                pending += text;

                if (sessionId is { } id && diagnostics is not null)
                {
                    // Keep enough overlap to redact raw or encoded agent tokens split across reads.
                    pending = await diagnostics.RedactAsync(id, pending, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    int length = Math.Max(val1: 0, pending.Length - 128);
                    await diagnostics.WriteOutputAsync(id, stream, pending[..length], cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
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
            if (sessionId is { } id)
                await (diagnostics?.WarnAsync(id, $"Console collection ended: {exception.Message}", cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            if (sessionId is { } id)
                await (diagnostics?.WriteOutputAsync(id, stream, pending, cancellationToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private static string CreateAgentArguments(GameTrackerConnection tracker)
    {
        string arguments = $"descriptor={EncodeAgentArgument(tracker.DescriptorPath)};token={EncodeAgentArgument(tracker.Token)}";

        return tracker.ExpectedName is null ? arguments : $"{arguments};name={EncodeAgentArgument(tracker.ExpectedName)}";
    }

    private static Uri CreateCurseForgeApiBaseUri()
    {
        string? configuredBaseUrl = Environment.GetEnvironmentVariable(variable: "CURSEFORGE_API_BASE_URL");

        string baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? DefaultCurseForgeApiBaseUrl
            : configuredBaseUrl.Trim();

        bool uniformResourceIdentifierCreated = Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri);

        if (!uniformResourceIdentifierCreated || baseUri is null)
            throw new InvalidOperationException(message: "CURSEFORGE_API_BASE_URL must be an absolute HTTP or HTTPS URL");

        bool schemeSupported = baseUri.Scheme is "http" or "https";

        if (!schemeSupported)
            throw new InvalidOperationException(message: "CURSEFORGE_API_BASE_URL must use HTTP or HTTPS");

        string path = baseUri.AbsolutePath.TrimEnd(trimChar: '/');

        return new UriBuilder(baseUri)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : path + "/",
            Query = "",
            Fragment = ""
        }.Uri;
    }

    private static ProcessStartInfo CreateProcessStartInformation(string fileName, IEnumerable<string> arguments, string? display = null)
    {
        ProcessStartInfo processStartInformation = new(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (display is not null)
            processStartInformation.Environment[key: "DISPLAY"] = display;

        foreach (string argument in arguments)
            processStartInformation.ArgumentList.Add(argument);

        return processStartInformation;
    }

    private static GameTrackerConnection CreateTrackerConnection(string? expectedName)
    {
        string descriptorPath = Path.Combine(Path.GetTempPath(), $"void-client-agent-{Guid.NewGuid():N}.port");

        return new GameTrackerConnection(descriptorPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(count: 32)), expectedName);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static string DetermineTargetDirectory(string fileName, string minecraftDirectory)
    {
        return fileName.EndsWith(value: ".jar", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(minecraftDirectory, path2: "mods")
            : fileName.EndsWith(value: ".zip", StringComparison.OrdinalIgnoreCase)
                ? fileName.Contains(value: "shader", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(minecraftDirectory, path2: "shaderpacks")
                    : Path.Combine(minecraftDirectory, path2: "resourcepacks")
                : Path.Combine(minecraftDirectory, path2: "mods");
    }

    private static async Task<byte[]> DownloadWithFallbackAsync(HttpClient hypertextTransferProtocolClient, string downloadUrl, string apiKey, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, downloadUrl);

        if (downloadUrl.Contains(value: "curseforge.com", StringComparison.OrdinalIgnoreCase))
            request.Headers.Add(name: "x-api-key", apiKey);

        using var response = await hypertextTransferProtocolClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var successfulResponse = response.EnsureSuccessStatusCode();

        return await successfulResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private static string EncodeAgentArgument(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd(trimChar: '=').Replace(oldChar: '+', newChar: '-').Replace(oldChar: '/', newChar: '_');
    }

    private static int? FindJavaProcessId(int rootProcessId)
    {
        HashSet<int> descendants = [rootProcessId];

        var processDirectories = Directory.EnumerateDirectories(path: "/proc")
            .Select(static path => (Path: path, Name: Path.GetFileName(path)))
            .Where(static item => int.TryParse(item.Name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToArray();

        bool changed = true;

        while (changed)
        {
            changed = false;

            foreach (var (path, name) in processDirectories)
            {
                int processId = int.Parse(name, CultureInfo.InvariantCulture);

                bool parentProcessFound = TryReadParentProcessId(path, out int parentProcessId);
                bool processIsNewDescendant = !descendants.Contains(processId) && parentProcessFound && descendants.Contains(parentProcessId);

                if (!processIsNewDescendant)
                    continue;

                bool descendantAdded = descendants.Add(processId);
                changed = true;
            }
        }

        foreach (int processId in descendants.OrderDescending())
        {
            try
            {
                string[] arguments = File.ReadAllText($"/proc/{processId}/cmdline").Split(separator: '\0', StringSplitOptions.RemoveEmptyEntries);

                if (arguments.Any(static argument => Path.GetFileName(argument) is "java" or "java-x86_64"))
                    return processId;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Process {processId} ended during command-line inspection: {exception.Message}");
            }
        }

        return null;
    }

    private static string? FindUsername(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] is "--username" or "-u")
                return index + 1 < arguments.Count ? arguments[index + 1] : null;

            if (arguments[index].StartsWith(value: "--username=", StringComparison.Ordinal))
                return arguments[index]["--username=".Length..];
        }

        return null;
    }

    private static string GetArmLwjglVersion(string version)
    {
        string[] versionComponents = version["mojang:".Length..].Split(separator: '.');

        return versionComponents.Length >= 1 && int.TryParse(versionComponents[0], out int majorVersion) && majorVersion >= 26
            ? PortableMinecraftArmLwjgl4Version
            : PortableMinecraftArmLwjgl3Version;
    }

    private static string GetMinecraftDirectory()
    {
        return Environment.GetEnvironmentVariable(variable: "MINECRAFT_DIRECTORY") ?? DefaultMinecraftDirectory;
    }

    private static bool HasPortableMinecraftArgument(IEnumerable<string> arguments, string argumentName)
    {
        return arguments.Any(
            argument => string.Equals(argument, argumentName, StringComparison.Ordinal) || argument.StartsWith($"{argumentName}=", StringComparison.Ordinal)
        );
    }

    private static async Task IgnoreTaskAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
