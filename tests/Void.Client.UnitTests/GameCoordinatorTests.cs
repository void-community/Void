using Microsoft.Extensions.Logging.Abstractions;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class GameCoordinatorTests
{
    [Fact]
    public async Task CoordinatorSupportsMultipleConfirmedGameSessions()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var firstLaunch = await coordinator.StartVanillaAsync(new("1.21.1", []), CancellationToken.None);
        Assert.Equal(GameState.Starting, firstLaunch.State);
        Assert.True(firstLaunch.OperationId > 0);

        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        var firstStop = await coordinator.StopGameAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(StopMode.Graceful, firstStop.Mode);
        Assert.Equal(GameState.Idle, firstStop.Status.State);

        var secondLaunch = await coordinator.StartVanillaAsync(new("1.20.1", []), CancellationToken.None);
        Assert.True(secondLaunch.OperationId > firstLaunch.OperationId);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var secondStop = await coordinator.StopGameAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(GameState.Idle, secondStop.Status.State);
        Assert.Equal(2, runtime.LaunchCount);
        Assert.Equal(2, runtime.StopCount);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentLaunchIsRejectedWithoutStartingAnotherProcess()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new(null, []), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartNeoForgeAsync(new(null, []), CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal(1, runtime.LaunchCount);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task InvalidLaunchIsRejectedBeforeRuntimeInvocation()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartVanillaAsync(new(" ", []), CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveMemoryIsRejectedBeforeRuntimeInvocation(int memoryMb)
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartVanillaAsync(new("1.21.1", [], memoryMb), CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TypedMemoryCannotConflictWithRawMaximumHeapArgument()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartVanillaAsync(new("1.21.1", ["--jvm-arg=-Xmx1G"], 2048), CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("vanilla")]
    [InlineData("neoforge")]
    [InlineData("curseforge")]
    public async Task LaunchForwardsConfiguredMemory(string launchType)
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        _ = launchType switch
        {
            "vanilla" => await coordinator.StartVanillaAsync(new("1.21.1", [], 2048), CancellationToken.None),
            "neoforge" => await coordinator.StartNeoForgeAsync(new("1.21.1", [], 2048), CancellationToken.None),
            "curseforge" => await coordinator.StartCurseForgeAsync(new("test", 1, [], 2048), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(launchType), launchType, null)
        };

        Assert.Equal(2048, runtime.LastMemoryMb);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConcurrentStopIsRejectedWithoutSupersedingTheAcceptedStop()
    {
        var runtime = new FakeGameRuntime { BlockStop = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartVanillaAsync(new("1.21.1", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var acceptedStop = coordinator.StopGameAsync(CancellationToken.None);
        await WaitForStateAsync(coordinator, GameState.Stopping);
        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StopGameAsync(CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        runtime.CompleteStop();
        var response = await acceptedStop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(GameState.Idle, response.Status.State);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeLaunchWithoutVersionRequestsLatest()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new(null, []), CancellationToken.None);

        Assert.Equal("", runtime.LastNeoForgeVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeLaunchForwardsRequestedVersion()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new("  1.21.1  ", []), CancellationToken.None);

        Assert.Equal("1.21.1", runtime.LastNeoForgeVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlayersReadReturnsLiveRuntimeDataWithoutChangingOperationStatus()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var acceptedLaunch = await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        var operationId = coordinator.Status.OperationId;

        var players = await coordinator.GetPlayersAsync(CancellationToken.None);

        Assert.Equal("local", players.Local.Name);
        Assert.Equal(new BodyRotation(10), players.Local.Body);
        Assert.Equal(new HeadRotation(15, -5), players.Local.Head);
        var remote = Assert.Single(players.Remote);
        Assert.Equal(new Position(4, 6, 3), remote.Position);
        Assert.Equal(new BodyRotation(20), remote.Body);
        Assert.Equal(new HeadRotation(25, 5), remote.Head);
        Assert.Equal(acceptedLaunch.OperationId, operationId);
        Assert.Equal(operationId, coordinator.Status.OperationId);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PlayersReadRequiresRunningGame()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.GetPlayersAsync(CancellationToken.None));

        Assert.Equal(409, exception.StatusCode);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SameTargetConnectCallsShareBackgroundOperationAndReplayResult()
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        using var firstCallerCancellation = new CancellationTokenSource();
        var firstConnect = coordinator.ConnectAsync(new("server", 25565), firstCallerCancellation.Token);
        await WaitForOperationAsync(coordinator, "connect");
        var secondConnect = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        firstCallerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstConnect);
        Assert.Equal(1, runtime.ConnectCount);
        Assert.False(runtime.ConnectCancellationRequested);

        runtime.CompleteConnect();
        var response = await secondConnect.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var replay = await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        Assert.Equal(response, replay);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        Assert.Equal(1, runtime.ConnectCount);
        Assert.Equal("test:1", runtime.LastConnectGame?.Version);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.ConnectAsync(new("other-server", 25565), CancellationToken.None));
        Assert.Equal(409, exception.StatusCode);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopCancelsSharedBackgroundConnect()
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var connect = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        await coordinator.StopGameAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        Assert.True(runtime.ConnectCancellationRequested);
        Assert.Equal(GameState.Idle, coordinator.Status.State);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SoleConnectCallerCancellationDetachesFromBackgroundOperation()
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        using var callerCancellation = new CancellationTokenSource();
        var connect = coordinator.ConnectAsync(new("server", 25565), callerCancellation.Token);
        await WaitForOperationAsync(coordinator, "connect");
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(runtime.ConnectCancellationRequested);

        runtime.CompleteConnect();
        await WaitForStateAsync(coordinator, GameState.Connected);
        var replay = await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        Assert.Equal("server", replay.Server.Host);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DetachedConnectFailureRemainsAvailableInStatus()
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        using var callerCancellation = new CancellationTokenSource();
        var connect = coordinator.ConnectAsync(new("server", 25565), callerCancellation.Token);
        await WaitForOperationAsync(coordinator, "connect");
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);

        runtime.FailConnect(new InvalidOperationException("synthetic client failure"));
        await WaitForOperationStateAsync(coordinator, OperationState.Failed);

        Assert.Equal("synthetic client failure", coordinator.Status.Error);
        Assert.NotNull(coordinator.Status.Failure);
        Assert.Contains("synthetic client failure", coordinator.Status.Failure.StackTrace);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NativeRejectionPreservesReasonAndAllowsAnotherConnection()
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.4", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var connection = coordinator.ConnectAsync(new("full-server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        runtime.FailConnect(new GameClientException("client.connect.rejected", "connect", "connection.rejected", "The server is full!"));
        var rejection = await Assert.ThrowsAsync<GameClientException>(() => connection);

        Assert.Equal("client.connect.rejected", rejection.Failure.Code);
        Assert.Equal("The server is full!", coordinator.Status.Failure?.Message);
        Assert.Equal(GameState.Ready, coordinator.Status.State);
        Assert.Equal(OperationState.Failed, coordinator.Status.OperationState);

        var nextConnection = coordinator.ConnectAsync(new("available-server", 25565), CancellationToken.None);
        await WaitForOperationStateAsync(coordinator, OperationState.Running);
        runtime.CompleteConnect();
        Assert.Equal("available-server", (await nextConnection).Server.Host);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(137, true, "client.process.out_of_memory")]
    [InlineData(137, false, "client.process.exited")]
    [InlineData(0, false, "client.process.exited")]
    public async Task UnexpectedProcessExitDuringConnectIsReportedToCaller(int exitCode, bool wasOutOfMemoryKilled, string expectedCode)
    {
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.17", [], 2048), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var connect = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        runtime.ExitGame(exitCode, wasOutOfMemoryKilled);

        var exception = await Assert.ThrowsAsync<GameProcessExitException>(() => connect);
        Assert.Equal(expectedCode, exception.Failure.Code);
        Assert.Contains($"exit code {exitCode}", exception.Message);

        if (wasOutOfMemoryKilled)
            Assert.Contains("configured maximum heap of 2048 MiB", exception.Message);

        await WaitForStateAsync(coordinator, GameState.Failed);
        Assert.Equal(GameState.Failed, coordinator.Status.State);
        Assert.Equal(exitCode, coordinator.Status.ExitCode);
        Assert.Equal(expectedCode, coordinator.Status.Failure?.Code);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SuccessfulProcessExitWithoutActiveOperationReturnsToIdle()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.17", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        runtime.ExitGame(0, false);

        await WaitForStateAsync(coordinator, GameState.Idle);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);
        Assert.Equal(0, coordinator.Status.ExitCode);
        Assert.Null(coordinator.Status.Failure);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DiagnosticsPreserveRejectedConnectionAfterStopAndRelaunch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"void-coordinator-diagnostics-{Guid.NewGuid()}");
        try
        {
            var diagnostics = new SessionDiagnostics(new DiagnosticsOptions { Directory = directory });
            var runtime = new FakeGameRuntime { BlockConnect = true, FailScreenshot = true };
            using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance, diagnostics);
            await coordinator.StartAsync(CancellationToken.None);
            var first = await coordinator.StartVanillaAsync(new("1.21", []), CancellationToken.None);
            Assert.NotNull(first.SessionId);
            runtime.CompleteLaunch();
            await WaitForStateAsync(coordinator, GameState.Ready);
            var connect = coordinator.ConnectAsync(new("limbo", 25565), CancellationToken.None);
            await WaitForOperationAsync(coordinator, "connect");
            runtime.FailConnect(new GameClientException("client.connect.rejected", "connect", "connection.rejected", "seven extra bytes"));
            await Assert.ThrowsAsync<GameClientException>(() => connect);
            var stopped = await coordinator.StopGameAsync(CancellationToken.None);
            Assert.Equal(first.SessionId, stopped.Status.SessionId);
            var second = await coordinator.StartVanillaAsync(new("1.21.1", []), CancellationToken.None);
            Assert.NotEqual(first.SessionId, second.SessionId);
            Assert.Equal("client.connect.rejected", (await diagnostics.ListAsync(TestContext.Current.CancellationToken)).Single(session => session.SessionId == first.SessionId).LastFailure?.Code);
            Assert.NotEmpty((await diagnostics.ListAsync(TestContext.Current.CancellationToken)).Single(session => session.SessionId == first.SessionId).Warnings);
            await coordinator.StopGameAsync(CancellationToken.None);
            await coordinator.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiagnosticsRetainEarlyLaunchFailureAndProcessExit(bool earlyFailure)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"void-coordinator-diagnostics-{Guid.NewGuid()}");
        try
        {
            var diagnostics = new SessionDiagnostics(new DiagnosticsOptions { Directory = directory });
            var runtime = new FakeGameRuntime();
            using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance, diagnostics);
            await coordinator.StartAsync(CancellationToken.None);
            var launch = await coordinator.StartVanillaAsync(new("1.21", []), CancellationToken.None);
            if (earlyFailure)
                runtime.FailLaunch(new InvalidOperationException("preparation failed"));
            else
            {
                runtime.CompleteLaunch();
                await WaitForStateAsync(coordinator, GameState.Ready);
                runtime.ExitGame(137, true);
            }
            await WaitForStateAsync(coordinator, GameState.Failed);
            var session = Assert.Single((await diagnostics.ListAsync(TestContext.Current.CancellationToken)));
            Assert.Equal(launch.SessionId, session.SessionId);
            Assert.NotNull(session.LastFailure);
            Assert.NotNull(session.EndedAt);
            await coordinator.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitForStateAsync(GameCoordinator coordinator, GameState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (coordinator.Status.State != expected)
            await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitForOperationStateAsync(GameCoordinator coordinator, OperationState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (coordinator.Status.OperationState != expected)
            await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitForOperationAsync(GameCoordinator coordinator, string operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (coordinator.Status.Operation != operation || coordinator.Status.OperationState is not OperationState.Running)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeGameRuntime : IGameRuntime
    {
        private TaskCompletionSource<RunningGame> _launch = CreateLaunchCompletion();
        private TaskCompletionSource<StopMode>? _stop;
        private TaskCompletionSource? _connect;
        private FakeManagedProcess? _process;

        public bool BlockStop { get; init; }
        public bool BlockConnect { get; init; }
        public bool FailScreenshot { get; init; }
        public int LaunchCount { get; private set; }
        public int StopCount { get; private set; }
        public int ConnectCount { get; private set; }
        public bool ConnectCancellationRequested { get; private set; }
        public int? LastMemoryMb { get; private set; }
        public string? LastVanillaVersion { get; private set; }
        public string? LastNeoForgeVersion { get; private set; }
        public RunningGame? LastConnectGame { get; private set; }

        public void FailLaunch(Exception exception) => _launch.SetException(exception);

        public void CompleteLaunch()
        {
            _process = new FakeManagedProcess(LaunchCount, LastMemoryMb);
            _launch.SetResult(new(_process, $"test:{LaunchCount}", DateTimeOffset.UtcNow, new("test.port", "token", null)));
        }

        public void CompleteStop()
        {
            _process?.Exit(0);
            _process = null;
            _stop?.SetResult(StopMode.Graceful);
        }

        public void CompleteConnect()
        {
            _connect?.SetResult();
        }

        public void FailConnect(Exception exception)
        {
            _connect?.SetException(exception);
        }

        public void ExitGame(int exitCode, bool wasOutOfMemoryKilled)
        {
            _process?.Exit(exitCode, wasOutOfMemoryKilled);
        }

        public Task WriteOptionsAsync(string options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
        {
            LastVanillaVersion = version;
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task<RunningGame> LaunchNeoForgeAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
        {
            LastNeoForgeVersion = version;
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
        {
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task ConnectAsync(RunningGame game, string host, int port, CancellationToken cancellationToken)
        {
            ConnectCount++;
            LastConnectGame = game;

            if (!BlockConnect)
                return Task.CompletedTask;

            _connect = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                ConnectCancellationRequested = true;
                _connect.TrySetCanceled(cancellationToken);
            });
            return _connect.Task;
        }

        public Task SendChatAsync(RunningGame game, string message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken) => FailScreenshot
            ? Task.FromException<byte[]>(new InvalidOperationException("Screenshot unavailable"))
            : Task.FromResult(Array.Empty<byte>());

        public Task<GamePlayers> ReadPlayersAsync(RunningGame game, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GamePlayers(
                new GamePlayer("local-id", "local", new Position(1, 2, 3), new BodyRotation(10), new HeadRotation(15, -5)),
                [new RemoteGamePlayer("other-id", "other", new Position(4, 6, 3), new BodyRotation(20), new HeadRotation(25, 5))]));
        }

        public Task<StopMode> StopAsync(RunningGame? game, CancellationToken cancellationToken)
        {
            StopCount++;

            if (BlockStop && game is not null)
            {
                _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _stop.Task;
            }

            _process?.Exit(0);
            _process = null;
            return Task.FromResult(game is null ? StopMode.AlreadyStopped : StopMode.Graceful);
        }

        private Task<RunningGame> BeginLaunch(CancellationToken cancellationToken)
        {
            LaunchCount++;
            _launch = CreateLaunchCompletion();
            cancellationToken.Register(() => _launch.TrySetCanceled(cancellationToken));
            return _launch.Task;
        }

        private static TaskCompletionSource<RunningGame> CreateLaunchCompletion()
        {
            return new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class FakeManagedProcess(int id, int? memoryMb) : IManagedProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; } = id;
        public bool HasExited { get; private set; }
        public int? ExitCode { get; private set; }
        public int? MemoryMb { get; } = memoryMb;
        public bool WasOutOfMemoryKilled { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

        public void KillTree() => Exit(137, false);

        public void Exit(int exitCode, bool wasOutOfMemoryKilled = false)
        {
            HasExited = true;
            ExitCode = exitCode;
            WasOutOfMemoryKilled = wasOutOfMemoryKilled;
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }
    }
}
