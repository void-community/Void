using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.IO;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

public record PortableMinecraftClient(IContainer Container, HttpClient HttpClient) : IIntegrationSide
{
    private const int ApiPort = 80;
    private const int ClientStatePollDelayMilliseconds = 250;
    private const string Display = ":99";
    private const string DockerHost = "host.docker.internal";
    private const string DockerHostGateway = "host-gateway";
    private const string LogMessagePrefix = $"[{nameof(Void)}.{nameof(IntegrationTests)}]";

    private readonly DateTime _readLogsSince = DateTime.UtcNow;

    public string LogFileName => "client.log";
    public IEnumerable<string> Logs => ReadLogsAsync(_readLogsSince).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        await Container.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    public async Task<IEnumerable<string>> ReadLogsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        return await Container.ReadLogsAsync(since, cancellationToken);
    }

    /// <param name="javaAgents">The Java agents every game of the client loads, which the client downloads.</param>
    public static async Task<PortableMinecraftClient> CreateAsync(IReadOnlyList<Uri> javaAgents, CancellationToken cancellationToken = default)
    {
        var image = Environment.GetEnvironmentVariable("VOID_CLIENT_IMAGE");
        var builder = new ContainerBuilder(image ?? "ghcr.io/void-community/portable-minecraft-client:offline")
            .WithImagePullPolicy(image is null ? PullPolicy.Always : PullPolicy.Missing)
            .WithEnvironment("DISPLAY", Display)
            .WithPortBinding(port: ApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort(ApiPort)
                    .ForPath("/api/health"), options => options.WithTimeout(TimeSpan.FromMinutes(1))));

        if (OperatingSystem.IsLinux())
            builder = builder.WithExtraHost(DockerHost, DockerHostGateway);

        if (javaAgents.Count > 0)
            builder = builder.WithEnvironment("JAVA_AGENTS", string.Join(',', javaAgents.Select(agent => agent.AbsoluteUri)));

        var container = builder.Build();

        await container.StartAsync(cancellationToken);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(ApiPort)}"),
            Timeout = Timeouts.SetupTimeout
        };

        return new PortableMinecraftClient(container, httpClient);
    }

    /// <param name="setup">The modded game to start, or <see langword="null"/> to start vanilla Minecraft of the protocol version.</param>
    public async Task<Game> LaunchGameAsync(ProtocolVersion protocolVersion, GameSetup? setup, CancellationToken cancellationToken = default)
    {
        return await Game.LaunchAsync(this, protocolVersion, setup, cancellationToken);
    }

    /// <summary>
    /// A launched Minecraft game, which plays one test after another until it stops. What its tests do is written by their
    /// <see cref="GameSession"/>, while the launch and the game's own diagnostics are written next to the steps of every test, under
    /// <c>steps/Games</c>.
    /// </summary>
    /// <param name="Setup">The modded game the client plays, or <see langword="null"/> when it plays vanilla Minecraft of the protocol version.</param>
    public record Game(PortableMinecraftClient Client, ProtocolVersion ProtocolVersion, GameSetup? Setup, string Username, DateTime LaunchedAt)
    {
        private const string ModsDirectory = "/root/.minecraft/mods/";
        private const string JvmOutputPath = "/root/jvm-output.log";
        private const string ThreadDumpHeader = "Full thread dump";
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(5);

        private ApiProblemDetails? _lastPlayersProblem;
        private Guid? _sessionId;

        public HttpClient HttpClient => Client.HttpClient;
        public IContainer Container => Client.Container;
        public string GameName => Setup?.ToString() ?? $"Minecraft {ProtocolVersion.FirstRelease}";

        /// <summary>Where the launch and the game's diagnostics are written.</summary>
        public string WorkingDirectory => StepsDirectory("Games", ProtocolVersion, Setup, Username);

        public static string StepsDirectory(string name, ProtocolVersion protocolVersion, GameSetup? setup, string username)
        {
            return setup is { } moddedSetup
                ? Path.Combine(Directory.GetCurrentDirectory(), "steps", name, moddedSetup.Loader.ToString(), protocolVersion.ToString(), username)
                : Path.Combine(Directory.GetCurrentDirectory(), "steps", name, protocolVersion.ToString(), username);
        }

        public static async Task<Game> LaunchAsync(PortableMinecraftClient client, ProtocolVersion protocolVersion, GameSetup? setup, CancellationToken cancellationToken = default)
        {
            var game = new Game(client, protocolVersion, setup, Convert.ToHexString(BitConverter.GetBytes(Random.Shared.Next())), DateTime.UtcNow);

            using var optionsContent = new StringContent(CreateOptionsText(protocolVersion), Encoding.UTF8, "text/plain");
            using var optionsResponse = await client.HttpClient.PutAsync("/api/game/options", optionsContent, cancellationToken);
            await EnsureSuccessAsync(optionsResponse, $"Writing Minecraft {protocolVersion.FirstRelease} options", cancellationToken);

            try
            {
                await LogAsync($"Starting {game.GameName}", cancellationToken);

                if (setup is not null)
                    await game.InstallModsAsync(setup, cancellationToken);

                await game.StartAsync(cancellationToken);
            }
            catch
            {
                await game.TryWriteLaunchLogsAsync(Timeouts.StepTimeoutToken);

                try
                {
                    await game.StopAsync(Timeouts.StepTimeoutToken);
                }
                catch (Exception exception)
                {
                    await LogAsync($"Cleanup after launch failure failed: {exception}", CancellationToken.None);
                }

                throw;
            }

            return game;
        }

        /// <summary>Stops the game, and saves its diagnostics with the launch's.</summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await LogAsync($"Stopping {GameName}", cancellationToken);

            try
            {
                using var response = await HttpClient.PostAsync("/api/game/stop", null, cancellationToken);
                await EnsureSuccessAsync(response, $"Stopping {GameName}", cancellationToken);
                var stopResponse = await response.Content.ReadFromJsonAsync<StopGameResponse>(cancellationToken)
                                   ?? throw new IntegrationTestException("Stopping Minecraft returned an empty response");

                if (stopResponse.Status.State is not "idle" || stopResponse.Status.OperationState is not "succeeded")
                    throw new IntegrationTestException($"Stopping Minecraft returned unexpected state {stopResponse.Status.State}/{stopResponse.Status.OperationState}");
            }
            finally
            {
                await ClientDiagnosticsDownloader.DownloadAsync(HttpClient, _sessionId, WorkingDirectory);
            }
        }

        /// <summary>Reads whether the game can begin another test, which it can while it's at its menu.</summary>
        public async Task<bool> IsPlayableAsync(CancellationToken cancellationToken = default)
        {
            var status = await ReadStatusAsync(cancellationToken);
            return status.State is "ready";
        }

        public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        {
            var status = await ReadStatusAsync(cancellationToken);
            return status.State is "connected";
        }

        public async Task JoinServerAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
        {
            var (dockerHost, dockerPort) = endPoint.AsDockerHostPort;

            await LogAsync($"Navigating the Minecraft UI to {dockerHost}:{dockerPort}", cancellationToken);

            using var response = await HttpClient.PostAsJsonAsync("/api/game/connect", new { host = dockerHost, port = dockerPort }, cancellationToken);
            await EnsureSuccessAsync(response, $"Navigating Minecraft {ProtocolVersion.FirstRelease} to {dockerHost}:{dockerPort}", cancellationToken);
        }

        public async Task JoinServerExpectingFailureAsync(EndPoint endPoint, string expectedReason, CancellationToken cancellationToken = default)
        {
            var (dockerHost, dockerPort) = endPoint.AsDockerHostPort;
            using var response = await HttpClient.PostAsJsonAsync("/api/game/connect", new { host = dockerHost, port = dockerPort }, cancellationToken);
            var problem = await ReadProblemDetailsAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode || problem?.Failure?.Code is not "client.connect.rejected" || problem.Detail?.Contains(expectedReason, StringComparison.Ordinal) is not true)
                throw new IntegrationTestException($"Minecraft {ProtocolVersion.FirstRelease} did not report the expected native rejection {expectedReason}: HTTP {(int)response.StatusCode}, {JsonSerializer.Serialize(problem)}");

            await LogAsync($"Minecraft {ProtocolVersion.FirstRelease} confirmed the native connection rejection: {problem.Detail}", cancellationToken);
        }

        /// <summary>Leaves the server, so the game can join one again.</summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await LogAsync($"Disconnecting {GameName}", cancellationToken);

            using var response = await HttpClient.PostAsync("/api/game/disconnect", null, cancellationToken);

            // Each test leaves its server so the next one can join again, which images older than the endpoint can't.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                throw new IntegrationTestException("The client image predates POST /api/game/disconnect");

            await EnsureSuccessAsync(response, $"Disconnecting {GameName}", cancellationToken);
        }

        public async Task SendChatAsync(string text, CancellationToken cancellationToken = default)
        {
            await LogAsync($"Sending chat input: {text}", cancellationToken);

            using var response = await HttpClient.PostAsJsonAsync("/api/game/send-chat", new { message = text }, cancellationToken);
            await EnsureSuccessAsync(response, $"Sending chat input: {text}", cancellationToken);
        }

        public async Task WaitForLogsSilenceAsync(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            var timestamp = Stopwatch.GetTimestamp();
            await LogAsync($"Waiting for logs to become stable for {duration.TotalSeconds:F2} seconds", cancellationToken);

            // Ensure the game is stable and not doing some background loading
            await Container.WaitForLogsSilenceAsync(duration, whitelist:
            [
                // Ignore log messages
                LogMessagePrefix,

                // Ignore chat messages (might be many clients on a single server)
                "[CHAT]",

                // Ignore spam messages that can appear because of poor ViaVersion protocol support
                "Unable to play unknown soundEvent", // Unable to play unknown soundEvent minecraft:mob.rabbit.hop (or .idle)
                " has no item?!", // Item entity 72 has no item?!
                "Skipping Entity with id" // Skipping Entity with id minecraft:cave_spider
            ], cancellationToken);

            var remainingDuration = duration - Stopwatch.GetElapsedTime(timestamp);

            if (remainingDuration > TimeSpan.Zero)
                await Task.Delay(remainingDuration, cancellationToken);

            await LogAsync($"Logs are stable after {Stopwatch.GetElapsedTime(timestamp).TotalSeconds:F2} seconds", cancellationToken);
        }

        public async Task<ApiGamePlayers?> TryReadPlayersAsync(CancellationToken cancellationToken = default)
        {
            using var response = await HttpClient.GetAsync("/api/game/players", cancellationToken);

            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable)
            {
                _lastPlayersProblem = await ReadProblemDetailsAsync(response, cancellationToken);
                return null;
            }

            await EnsureSuccessAsync(response, "Reading Minecraft players", cancellationToken);
            _lastPlayersProblem = null;
            return await response.Content.ReadFromJsonAsync<ApiGamePlayers>(cancellationToken)
                   ?? throw new IntegrationTestException("Reading Minecraft players returned an empty response");
        }

        public async Task<string> ReadDiagnosticsAsync()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                using var response = await HttpClient.GetAsync("/api/game/status", cancellation.Token);
                var statusContent = await response.Content.ReadAsStringAsync(cancellation.Token);
                return JsonSerializer.Serialize(new
                {
                    version = ProtocolVersion.FirstRelease.ToString(),
                    username = Username,
                    sessionId = _sessionId,
                    statusCode = (int)response.StatusCode,
                    status = TryDeserialize<ApiStatus>(statusContent),
                    rawStatus = statusContent,
                    lastPlayersProblem = _lastPlayersProblem
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            }
            catch (Exception exception)
            {
                return $"Failed to read Minecraft client diagnostics: {exception}";
            }
        }

        public async Task<byte[]> TakeScreenshotAsync(CancellationToken cancellationToken = default)
        {
            using var response = await HttpClient.GetAsync("/api/game/screenshot", cancellationToken);
            await EnsureSuccessAsync(response, "Taking Minecraft screenshot", cancellationToken);

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        public static Task LogAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"{LogMessagePrefix} {message}");
            return Task.CompletedTask;
        }

        private async Task StartAsync(CancellationToken cancellationToken = default)
        {
            var newerRelease = ProtocolVersion >= ProtocolVersion.MINECRAFT_26_1;

            // Mod loaders and their mods need more memory than the game alone.
            var memoryMb = Setup is null ? newerRelease ? 2048 : 1024 : newerRelease ? 3072 : 2048;
            var version = Setup?.MinecraftRelease.ToString() ?? ProtocolVersion.FirstRelease.ToString();
            var arguments = new[]
            {
                "--fetch-exclude-all",
                "--username",
                Username,
                "--jvm-arg=-Djava.awt.headless=false",
                // The launcher cuts the game's output short at the first lock address of a thread dump, so the JVM also writes its own output
                // into a file.
                "--jvm-arg=-XX:+UnlockDiagnosticVMOptions",
                "--jvm-arg=-XX:+LogVMOutput",
                $"--jvm-arg=-XX:LogFile={JvmOutputPath}"
            };

            // The client's game jar of Forge for Minecraft 1.7.2 carries no Mojang signature, which its FML refuses to launch unless told to ignore it.
            if (Setup is { } forgeSetup && LegacyForgePatcher.AppliesTo(forgeSetup.Loader, forgeSetup.MinecraftRelease))
                arguments = [.. arguments, "--jvm-arg=-Dfml.ignoreInvalidMinecraftCertificates=true"];

            // The client starts a loader build as the servers report it, with its Minecraft version.
            using var response = Setup switch
            {
                null => await HttpClient.PostAsJsonAsync("/api/game/start/vanilla", new { version, memoryMb, arguments }, cancellationToken),
                { } setup => await HttpClient.PostAsJsonAsync($"/api/game/start/{setup.Loader.ToString().ToLowerInvariant()}", new { version, loaderVersion = setup.LoaderVersion, memoryMb, arguments }, cancellationToken)
            };
            await EnsureSuccessAsync(response, $"Starting {GameName}", cancellationToken);
            var acceptedStatus = await ReadApiStatusAsync(response, $"Accepting {GameName} launch", cancellationToken);

            _sessionId = acceptedStatus.SessionId;

            // A game that hasn't reached its menu by then won't, which leaves the setup timeout room to launch it again.
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyTimeout.CancelAfter(ReadyTimeout);

            try
            {
                await WaitForClientReadyAsync(acceptedStatus.OperationId, readyTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await TryDumpThreadsAsync();
                throw new IntegrationTestException($"{GameName} did not reach its menu within {ReadyTimeout.TotalMinutes} minutes");
            }
        }

        // A JVM writes where each of its threads is, and which locks they hold, to its output on SIGQUIT, so a game that hangs shows where.
        private async Task TryDumpThreadsAsync()
        {
            try
            {
                await Container.RunCommandAsync(["pkill", "--signal", "QUIT", "--exact", "java"], Timeouts.StepTimeoutToken);

                // The JVM writes the dump on a thread of its own after the signal.
                await Task.Delay(TimeSpan.FromSeconds(2));

                // The file starts with the game's command line, which holds the client agent's token, so only the dump is kept.
                var output = Encoding.UTF8.GetString(await Container.ReadFileAsync(JvmOutputPath, Timeouts.StepTimeoutToken));
                var dumpStart = output.LastIndexOf(ThreadDumpHeader, StringComparison.Ordinal);

                if (dumpStart < 0)
                {
                    await LogAsync($"{GameName} wrote no thread dump", CancellationToken.None);
                    return;
                }

                // The file is XML, which escapes the dump's text and puts lines of its own in between.
                var dump = WebUtility.HtmlDecode(string.Join('\n', output[dumpStart..].Split('\n').Where(line => !line.StartsWith('<'))));
                var dumpPath = Path.Combine(WorkingDirectory, "threads.log");

                Directory.CreateDirectory(WorkingDirectory);
                await File.WriteAllTextAsync(dumpPath, dump, Timeouts.StepTimeoutToken);
                await LogAsync($"Dumped {GameName} threads into {dumpPath}", CancellationToken.None);
            }
            catch (Exception exception)
            {
                await LogAsync($"Dumping {GameName} threads failed: {exception.Message}", CancellationToken.None);
            }
        }

        /// <summary>Installs the setup's mods into the client, replacing those of an earlier game, since the client keeps mods it didn't add.</summary>
        private async Task InstallModsAsync(GameSetup setup, CancellationToken cancellationToken = default)
        {
            await Container.RunCommandAsync(["sh", "-c", $"rm -rf {ModsDirectory} && mkdir -p {ModsDirectory}"], cancellationToken);

            foreach (var mod in setup.Mods)
                await Container.CopyAsync(mod.Content, $"{ModsDirectory}{mod.FileName}", ct: cancellationToken);

            Directory.CreateDirectory(WorkingDirectory);
            var setupJson = JsonSerializer.Serialize(new
            {
                minecraftRelease = setup.MinecraftRelease.ToString(),
                loader = setup.Loader.ToString(),
                loaderVersion = setup.LoaderVersion,
                mods = setup.Mods.Select(mod => mod.FileName)
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(WorkingDirectory, "game-setup.json"), setupJson, cancellationToken);
            await LogAsync($"Installed mods {string.Join(", ", setup.Mods.Select(mod => mod.FileName))}", cancellationToken);
        }

        private async Task<ApiStatus> ReadStatusAsync(CancellationToken cancellationToken = default)
        {
            using var response = await HttpClient.GetAsync("/api/game/status", cancellationToken);
            await EnsureSuccessAsync(response, "Reading Minecraft client status", cancellationToken);

            return await ReadApiStatusAsync(response, "Reading Minecraft client status", cancellationToken);
        }

        private async Task WaitForClientReadyAsync(long operationId, CancellationToken cancellationToken = default)
        {
            var status = await ReadStatusAsync(cancellationToken);

            while (status.OperationId != operationId || status is not { State: "ready", OperationState: "succeeded" })
            {
                if (status.OperationId != operationId)
                    throw new IntegrationTestException($"Minecraft launch operation {operationId} was superseded by {status.OperationId}");

                if (status.State is "failed" || status.OperationState is "failed" or "canceled")
                    throw new IntegrationTestException($"Starting Minecraft {ProtocolVersion.FirstRelease} failed: {status.Error ?? "client entered failed state"}");

                await Task.Delay(TimeSpan.FromMilliseconds(ClientStatePollDelayMilliseconds), cancellationToken);
                status = await ReadStatusAsync(cancellationToken);
            }
        }

        private async Task TryWriteLaunchLogsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(WorkingDirectory);
                var logs = await Client.ReadLogsAsync(LaunchedAt, cancellationToken);
                await File.WriteAllLinesAsync(Path.Combine(WorkingDirectory, Client.LogFileName), logs, cancellationToken);
            }
            catch (Exception exception)
            {
                await LogAsync($"Failed to write launch logs: {exception.Message}", CancellationToken.None);
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken = default)
        {
            if (response.IsSuccessStatusCode)
                return;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // The client's own failure leads the message, so a test's summary names the cause rather than only the status
            var cause = TryDeserialize<ApiProblemDetails>(content) switch
            {
                { Failure: { } failure } => $": {failure.Code}: {failure.Message}",
                { Detail: { } detail } => $": {detail}",
                _ => ""
            };

            throw new IntegrationTestException($"{operation} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}{cause}\n{content}");
        }

        private static async Task<ApiStatus> ReadApiStatusAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken = default)
        {
            return await TryReadApiStatusAsync(response, cancellationToken)
                ?? throw new IntegrationTestException($"{operation} returned an empty or malformed status response");
        }

        private static async Task<ApiStatus?> TryReadApiStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ApiStatus>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static async Task<ApiProblemDetails?> ReadProblemDetailsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return TryDeserialize<ApiProblemDetails>(content)
                   ?? new(response.ReasonPhrase, (int)response.StatusCode, content, null);
        }

        private static T? TryDeserialize<T>(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static string CreateOptionsText(ProtocolVersion protocolVersion)
        {
            var options = new List<string>
            {
                "maxFps:15",
                "renderDistance:2",
                "pauseOnLostFocus:false"
            };

            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19_4)
                options.Insert(2, "onboardAccessibility:false");

            return string.Join('\n', options) + "\n";
        }

        private record ApiFailure(string Code, string Operation, string Stage, string Message, string ExceptionType, string StackTrace);

        private record ApiProblemDetails(string? Title, int? Status, string? Detail, ApiFailure? Failure);

        private record ApiStatus(string State, long OperationId, string? Operation, string OperationState, int? ProcessId, int? ExitCode, string? Message, string? Error, ApiFailure? Failure, DateTimeOffset UpdatedAt, Guid? SessionId = null);

        private record StopGameResponse(string Mode, ApiStatus Status);

        public record ApiPosition(double X, double Y, double Z);

        public record ApiBodyRotation(double Yaw);

        public record ApiHeadRotation(double Yaw, double Pitch);

        public record ApiGamePlayer(string? Uuid, string? Name, ApiPosition Position, ApiBodyRotation Body, ApiHeadRotation Head);

        public record ApiRemoteGamePlayer(string? Uuid, string? Name, ApiPosition Position, ApiBodyRotation Body, ApiHeadRotation Head);

        public record ApiGamePlayers(ApiGamePlayer Local, IReadOnlyList<ApiRemoteGamePlayer> Remote);
    }
}
