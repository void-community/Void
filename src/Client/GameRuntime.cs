using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

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
    private const string AzulPackagesUrl = "https://api.azul.com/metadata/v1/zulu/packages/";
    private const int BlockingWindowPollMilliseconds = 2000;
    private const int BlockingWindowTimeoutMilliseconds = 10000;
    private const string CurseForgeCdnUrl = "https://edge.forgecdn.net";
    private const int CurseForgeFilesBatchSize = 50;
    // The installed modpack is "<slug> <fileId>" in this file, which only exists while the game directory holds that modpack.
    private const string CurseForgeMarkerFileName = ".curseforge-modpack";
    // Lists the installed modpack's override files, so the next modpack can remove the ones it doesn't have.
    private const string CurseForgeOverridesFileName = ".curseforge-overrides";
    private const int CurseForgeSha1HashAlgorithm = 1;
    private const string CurseForgeVersionFileName = ".curseforge-portablemc-version";
    private const string CurseForgeWebsiteUrl = "https://www.curseforge.com";
    private const string DefaultCurseForgeApiBaseUrl = "https://api.curseforge.com";
    private const int DefaultCurseForgeParallelDownloads = 4;
    private const string DefaultDisplay = ":99";
    private const string DefaultMinecraftDirectory = "/root/.minecraft";
    private const int DisplayProbeTimeoutMilliseconds = 1000;
    private const string DisplayScreenHeight = "480";
    private const string DisplayScreenResolution = $"{DisplayScreenWidth}x{DisplayScreenHeight}";
    private const string DisplayScreenWidth = "854";
    private const int ExternalProcessTimeoutMilliseconds = 5000;
    // Minecraft prints this before the path of a crash report it saved, right before it exits. Launchers watch for it too.
    private const string GameCrashMarker = "#@!@# Game crashed! Crash report saved to: #@!@# ";
    // Helpers in the image, such as the xmessage shim, report non-fatal problems on the game's output with this prefix.
    private const string GameWarningPrefix = "Void client warning: ";
    private const string LauncherSplashWindowTitle = "Void Client Startup";
    private const string LegacyNeoForgeMavenMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml";
    private const string MavenCentralRepositoryUrl = "https://repo1.maven.org/maven2";
    private const int MaximumGameOutputLineLength = 64 * 1024;
    private const int MinecraftGameId = 432;
    // The installed CurseForge modpack runs in this folder of the game directory, which keeps its mods and configs to itself.
    private const string ModpackDirectoryName = "modpack";
    private const string MojangVersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string NeoForgeMavenMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const int PlayerReadTimeoutMilliseconds = 2000;
    private const string PortableMinecraftAgentPath = "/opt/portableminecraftclient/void-client-agent.jar";
    private const string PortableMinecraftArmLwjglMinimumVersion = "3.3.3";
    private const string PortableMinecraftArmNativesClassifier = "natives-linux-arm64";
    private const string PortableMinecraftBakedFilesDirectory = "/opt/portableminecraftclient";
    private const string PortableMinecraftDryRunPath = "/usr/local/bin/portablemc-dry-run-with-retries";
    private const string PortableMinecraftJvmAttachPath = "/usr/bin/jattach";
    private const string PortableMinecraftJvmDirectory = "/usr/lib/jvm";
    private const string PortableMinecraftLauncherPath = "/usr/local/bin/launch-portableminecraftclient";
    private const string PortableMinecraftLegacyJvmExecutablePath = "/usr/lib/jvm/zulu-8/bin/java";
    private const string PortableMinecraftLegacyJvmPath = "/usr/local/bin/java-arm64-lwjgl2";
    private const string PortableMinecraftOptionsPath = "/opt/portableminecraftclient/options.txt";
    private const string PortableMinecraftSodiumOptionsPath = "/opt/portableminecraftclient/sodium-options.json";
    private const int ProcessStopTimeoutMilliseconds = 10000;
    private const int ScreenCaptureMaximumAttempts = 3;
    private const int ScreenCaptureTimeoutMilliseconds = 10000;
    private const int UpstreamRequestAttempts = 3;
    private const string UpstreamUserAgent = "caunt/Void";
    private const int WindowPollMilliseconds = 250;
    private static readonly JsonSerializerOptions CurseForgeManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    // A game that printed nothing for this long has stopped responding. Loading games log as they go, however long they take.
    private static readonly TimeSpan OutputSilenceLimit = TimeSpan.FromMinutes(minutes: 5);
    private static readonly JsonSerializerOptions TrackerJsonOptions = new(JsonSerializerDefaults.Web);
    // Each retry of an upstream request waits this much longer than the one before.
    private static readonly TimeSpan UpstreamRetryDelay = TimeSpan.FromSeconds(seconds: 2);
    private readonly AsyncLock _windowOperations = new();
    private readonly ConcurrentDictionary<int, Task> _outputTasks = new();
    private readonly ConcurrentDictionary<int, ChannelReader<string>> _outputWarnings = new();
    private readonly ConcurrentDictionary<int, CancellationToken> _outputSilenceTokens = new();
    private readonly ConcurrentDictionary<int, string> _launchedProcessGroups = new();
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

    public string GetGameDirectory()
    {
        return Environment.GetEnvironmentVariable(variable: "MINECRAFT_DIRECTORY") ?? DefaultMinecraftDirectory;
    }

    public string GetModpackDirectory()
    {
        return Path.Combine(GetGameDirectory(), ModpackDirectoryName);
    }

    public async Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        string? apiKey = Environment.GetEnvironmentVariable(variable: "CURSEFORGE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(message: "CURSEFORGE_API_KEY is not set");

        int parallelDownloads = GetCurseForgeParallelDownloads(Environment.GetEnvironmentVariable(variable: "CURSEFORGE_PARALLEL_DOWNLOADS"));
        string minecraftDirectory = GetGameDirectory();
        string modpackDirectory = GetModpackDirectory();
        string portableMinecraftVersion;

        // The modpack runs in its own directory, so vanilla and NeoForge starts in the game directory never load its mods or configs.
        MoveModpackIntoItsDirectory(minecraftDirectory, modpackDirectory);

        using (HttpClient httpClient = CreateUpstreamHttpClient())
        {
            portableMinecraftVersion = await PrepareCurseForgeAsync(
                httpClient,
                slug,
                fileId,
                apiKey,
                CreateCurseForgeApiBaseUri(),
                parallelDownloads,
                modpackDirectory,
                UpstreamRetryDelay,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);
        }

        // A modpack without options of its own starts with the consumer's, which also turn off Minecraft's first-launch screens.
        string modpackOptionsPath = Path.Combine(modpackDirectory, path2: "options.txt");
        string optionsPath = Path.Combine(minecraftDirectory, path2: "options.txt");

        if (!File.Exists(modpackOptionsPath) && File.Exists(optionsPath))
            File.Copy(optionsPath, modpackOptionsPath);

        var armLaunchOptions = await PrepareMinecraftAsync(
            () => PrepareArmLaunchOptionsAsync(minecraftDirectory, GetMinecraftVersionFromPortableVersion(portableMinecraftVersion), cancellationToken),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        // The modpack brings its own mods and options, so only Minecraft and its mod loader are prepared, where downloads are retried.
        string preparedMinecraftVersion = await RunPortableMinecraftDryRunAsync(minecraftDirectory, portableMinecraftVersion, armLaunchOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await Console.Error.WriteLineAsync($"Prepared Minecraft {preparedMinecraftVersion} for the modpack").ConfigureAwait(continueOnCapturedContext: false);

        return await LaunchGameAsync(minecraftDirectory, modpackDirectory, portableMinecraftVersion, armLaunchOptions, arguments, memoryMb, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<RunningGame> LaunchNeoForgeAsync(
        string version,
        string? maximumMinecraftVersion,
        IReadOnlyList<string> arguments,
        int? memoryMb,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrWhiteSpace(version))
            return await LaunchPortableAsync($"neoforge:{version}", minecraftVersion: null, arguments, memoryMb, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var selection = await PrepareMinecraftAsync(() => ResolveLatestStableNeoForgeAsync(maximumMinecraftVersion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        // An exact loader build keeps the dry run and the launch on the same NeoForge version.
        return await LaunchPortableAsync($"neoforge::{selection.NeoForgeVersion}", selection.MinecraftVersion, arguments, memoryMb, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
    {
        return LaunchPortableAsync($"mojang:{version}", minecraftVersion: null, arguments, memoryMb, cancellationToken);
    }

    public async Task<string?> ReadDisconnectReasonAsync(RunningGame game, CancellationToken cancellationToken)
    {
        var response = await QueryAgentAsync(game, command: "connection", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return response?.Status switch
        {
            "ok" => null,
            "disconnected" => response.Message ?? "The game left the server",
            _ => throw new InvalidOperationException(response?.Message ?? "The Minecraft agent did not report the connection")
        };
    }

    public long? ReadMemoryLimitMb()
    {
        return CgroupMemory.ReadMemoryLimitMb();
    }

    public async Task<GameMenu?> ReadMenuAsync(RunningGame game, CancellationToken cancellationToken)
    {
        // The agent publishes its endpoint before the game opens a window, so without one it didn't start with the game, and is attached the
        // way every other agent command attaches it. A failure is the answer, so the menu monitor doesn't attach again every second.
        if (!File.Exists(game.Tracker.DescriptorPath))
        {
            try
            {
                await AttachAgentAsync(game, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                return new GameMenu(Screen: null, $"The client can't follow the game's screens: {exception.Message.Trim()}");
            }

            if (!File.Exists(game.Tracker.DescriptorPath))
                return new GameMenu(Screen: null, Problem: "The Minecraft agent is not running, so the client can't follow the game's screens");
        }

        var response = await QueryAgentAsync(game, command: "ready", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return response?.Status switch
        {
            "ok" => new GameMenu(response.Value, Problem: null),
            "loading" => null,
            "unavailable" when response.Stage is "code.discovery" => new GameMenu(Screen: null, response.Message),
            _ => throw new InvalidOperationException(response?.Message ?? "The Minecraft agent did not report the game's menu")
        };
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

            using TcpClient client = await ConnectToAgentAsync(port, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

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
        string minecraftDirectory = GetGameDirectory();
        string modpackDirectory = GetModpackDirectory();
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));
        await WriteOptionsFileAsync(minecraftDirectory, options, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        // The options apply to the next launch of any kind, and the installed modpack runs in its own directory.
        if (Directory.Exists(modpackDirectory))
            await WriteOptionsFileAsync(modpackDirectory, options, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    internal static string CreateMaximumHeapArgument(int memoryMb)
    {
        return $"--jvm-arg=-Xmx{memoryMb.ToString(CultureInfo.InvariantCulture)}M";
    }

    /// <summary>
    /// Reports each complete line that starts with <see cref="GameWarningPrefix"/>, and the crash report path of each line with
    /// <see cref="GameCrashMarker"/>, and returns the unfinished last line.
    /// </summary>
    internal static string ReadGameOutput(string text, ChannelWriter<string> warnings, Action<string>? crashReported = null)
    {
        int lineStart = 0;
        int lineEnd = text.IndexOf(value: '\n', lineStart);

        while (lineEnd >= 0)
        {
            var line = text.AsSpan(lineStart, lineEnd - lineStart).TrimEnd(trimChar: '\r');
            bool isWarning = line.StartsWith(GameWarningPrefix, StringComparison.Ordinal);

            // The channel only refuses warnings once the game's output has ended, so nothing would read the rest either.
            if (isWarning && !warnings.TryWrite(line[GameWarningPrefix.Length..].ToString()))
                return "";

            // Launchers and loaders put their own prefix before the marker.
            int crashMarker = line.IndexOf(GameCrashMarker, StringComparison.Ordinal);

            if (crashMarker >= 0)
                crashReported?.Invoke(line[(crashMarker + GameCrashMarker.Length)..].Trim().ToString());

            lineStart = lineEnd + 1;
            lineEnd = text.IndexOf(value: '\n', lineStart);
        }

        // Output without line breaks keeps growing, but only a short unfinished line can still become a warning.
        return text.Length - lineStart > MaximumGameOutputLineLength ? "" : text[lineStart..];
    }

    internal async Task PumpOutputAsync(
        TextReader reader,
        TextWriter console,
        Guid? sessionId,
        string stream,
        ChannelWriter<string>? warnings = null,
        Action<string>? crashReported = null,
        Action? outputRead = null,
        CancellationToken cancellationToken = default
    )
    {
        char[] buffer = new char[4096];
        string pending = "";
        string unfinishedLine = "";
        ConsoleTextNormalizer normalizer = new();

        try
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            while (count > 0)
            {
                outputRead?.Invoke();
                string text = normalizer.Normalize(buffer.AsSpan(start: 0, count));
                await console.WriteAsync(text).ConfigureAwait(continueOnCapturedContext: false);
                pending += text;

                if (warnings is not null)
                    unfinishedLine = ReadGameOutput(unfinishedLine + text, warnings, crashReported);

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
            string remainder = normalizer.Flush();
            await console.WriteAsync(remainder).ConfigureAwait(continueOnCapturedContext: false);
            pending += remainder;

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

    private static string EncodeAgentArgument(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd(trimChar: '=').Replace(oldChar: '+', newChar: '-').Replace(oldChar: '/', newChar: '_');
    }

    private static int? FindJavaProcessId(int rootProcessId)
    {
        HashSet<int> descendants = [rootProcessId];

        var processDirectories = Directory.EnumerateDirectories(path: "/proc")
            .Select(static path => new ProcessDirectory(path, Path.GetFileName(path)))
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

    private static async Task<TrackerResponse?> QueryAgentAsync(RunningGame game, string command, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromMilliseconds(PlayerReadTimeoutMilliseconds));

        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        string descriptor = await File.ReadAllTextAsync(game.Tracker.DescriptorPath, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

        if (!int.TryParse(descriptor, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new InvalidOperationException(message: "The Minecraft agent published an invalid endpoint");

        using TcpClient client = await ConnectToAgentAsync(port, linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

        using var stream = client.GetStream();

        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

        using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync($"{game.Tracker.Token}\t{command}".AsMemory(), linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);
        string? responseJson = await reader.ReadLineAsync(linkedSource.Token).ConfigureAwait(continueOnCapturedContext: false);

        return string.IsNullOrWhiteSpace(responseJson) ? null : JsonSerializer.Deserialize<TrackerResponse>(responseJson, TrackerJsonOptions);
    }

    private static async Task WriteOptionsFileAsync(string directory, string options, CancellationToken cancellationToken)
    {
        string destinationPath = Path.Combine(directory, path2: "options.txt");
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

    private async Task PumpGameOutputAsync(Process process, string gameDirectory, Guid? sessionId, ChannelWriter<string> warnings)
    {
        DateTime launchedAt = DateTime.UtcNow;
        int crashed = 0;
        Task? crashedGameEnd = null;

        // Every read restarts it, so it's canceled once the game printed nothing for the whole limit. It's disposed with the output, so a
        // game that exited never counts as silent.
        using CancellationTokenSource outputSilence = new(OutputSilenceLimit);

        _outputSilenceTokens[process.Id] = outputSilence.Token;

        void CrashReported(string reportPath)
        {
            // Chat is logged to the same output, so only a crash report this game saved counts.
            if (CrashReports.IsReport(reportPath, gameDirectory, launchedAt) && Interlocked.Exchange(ref crashed, value: 1) is 0)
                crashedGameEnd = EndCrashedGameAsync(process);
        }

        void OutputRead()
        {
            outputSilence.CancelAfter(OutputSilenceLimit);
        }

        try
        {
            // PortableMC may merge the game's streams, so warnings are read from both.
            await Task.WhenAll(
                PumpOutputAsync(process.StandardOutput, Console.Out, sessionId, stream: "stdout", warnings, CrashReported, OutputRead),
                PumpOutputAsync(process.StandardError, Console.Error, sessionId, stream: "stderr", warnings, CrashReported, OutputRead)
            ).ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            warnings.Complete();

            if (crashedGameEnd is not null)
                await crashedGameEnd.ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private readonly record struct ProcessDirectory(string Path, string Name);
}
