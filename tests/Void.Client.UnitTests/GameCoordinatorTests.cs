using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class GameCoordinatorTests
{
    [Fact]
    public async Task GameWarningsReachStatusOnlyForTheCurrentGame()
    {
        const string DialogWarning = "Dialog \"Memory Settings\" answered with Cancel: low memory";
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartVanillaAsync(new("1.21.1", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        var firstGameWarnings = runtime.Warnings;
        Assert.True(firstGameWarnings.Writer.TryWrite(DialogWarning));
        Assert.True(firstGameWarnings.Writer.TryWrite(DialogWarning));
        await WaitForWarningsAsync(coordinator, count: 1);
        Assert.Equal([DialogWarning], coordinator.Status.Warnings);

        await coordinator.StopGameAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await coordinator.StartVanillaAsync(new("1.21.1", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        Assert.True(firstGameWarnings.Writer.TryWrite("late warning from the stopped game"));
        Assert.True(runtime.Warnings.Writer.TryWrite("current warning"));
        await WaitForWarningsAsync(coordinator, count: 1);
        Assert.Equal(["current warning"], coordinator.Status.Warnings);

        await coordinator.StopGameAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await coordinator.StopAsync(CancellationToken.None);
    }

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
    [InlineData(3000, 2048L, false)]
    [InlineData(2048, 2048L, false)]
    [InlineData(2047, 2048L, true)]
    [InlineData(3000, null, true)]
    public async Task MemoryMustBeLowerThanTheContainerLimit(int memoryMb, long? limitMb, bool launched)
    {
        // Issue #1614: a heap the container can't hold ends with the game, or the whole container, killed for running out of memory.
        var runtime = new FakeGameRuntime { MemoryLimitMb = limitMb };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        if (launched)
        {
            await coordinator.StartVanillaAsync(new("1.21.1", [], memoryMb), CancellationToken.None);
            runtime.CompleteLaunch();
            await WaitForStateAsync(coordinator, GameState.Ready);

            Assert.Equal(memoryMb, runtime.LastMemoryMb);

            await coordinator.StopGameAsync(CancellationToken.None);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartVanillaAsync(new("1.21.1", [], memoryMb), CancellationToken.None));

            Assert.Equal(400, exception.StatusCode);
            Assert.Equal($"memoryMb must be lower than the container's memory limit of {limitMb} MiB", exception.Message);
            Assert.Equal(0, runtime.LaunchCount);
        }

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("vanilla")]
    [InlineData("neoforge")]
    [InlineData("curseforge")]
    [InlineData("forge")]
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
            "forge" => await coordinator.StartModLoaderAsync(loader: "forge", new("1.20.1", LoaderVersion: null, [], 2048), CancellationToken.None),
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
        Assert.Null(runtime.LastNeoForgeMaximumMinecraftVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeLaunchForwardsMaximumMinecraftVersion()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new(null, [], MaximumMinecraftVersion: " 26.2 "), CancellationToken.None);

        Assert.Equal("", runtime.LastNeoForgeVersion);
        Assert.Equal("26.2", runtime.LastNeoForgeMaximumMinecraftVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeLaunchRejectsMaximumMinecraftVersionWithExplicitVersion()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.StartNeoForgeAsync(new("26.3", [], MaximumMinecraftVersion: "26.2"), CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);

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
    public async Task NeoForgeLaunchForwardsAnExactBuild()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new(Version: null, [], LoaderVersion: " 21.1.264 "), CancellationToken.None);

        Assert.Equal("21.1.264", runtime.LastNeoForgeLoaderVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeLaunchForwardsABuildWithItsMinecraftVersion()
    {
        // Servers name a NeoForge build for Minecraft 1.20.1 without the Minecraft version in front.
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.StartNeoForgeAsync(new("1.20.1", [], LoaderVersion: "47.1.106"), CancellationToken.None);

        Assert.Equal("1.20.1", runtime.LastNeoForgeVersion);
        Assert.Equal("47.1.106", runtime.LastNeoForgeLoaderVersion);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NeoForgeBuildCannotBeCombinedWithAMaximumMinecraftVersion()
    {
        // A NeoForge build belongs to one Minecraft version, so there is no newest version to limit.
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(
            () => coordinator.StartNeoForgeAsync(new(Version: null, [], MaximumMinecraftVersion: "1.21.1", LoaderVersion: "21.1.264"), CancellationToken.None)
        );

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ModLoaderLaunchForwardsTheLoaderAndItsBuild()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var status = await coordinator.StartModLoaderAsync(loader: "forge", new(" 1.20.1 ", " 47.4.0 ", []), CancellationToken.None);

        Assert.Equal("start-forge", status.Operation);
        Assert.Equal(("forge", "1.20.1", "47.4.0"), runtime.LastModLoaderLaunch);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ModLoaderLaunchRequiresAVersion()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<GameCommandException>(
            () => coordinator.StartModLoaderAsync(loader: "fabric", new(Version: null, "0.16.5", []), CancellationToken.None)
        );

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(0, runtime.LaunchCount);

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
    public async Task VoidOperationsCompleteWithoutSyntheticResults()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.WriteOptionsAsync("difficulty=hard", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("options", coordinator.Status.Operation);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);

        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await coordinator.SendChatAsync(new("hello"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("send-chat", coordinator.Status.Operation);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);

        await coordinator.StopGameAsync(CancellationToken.None);
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

        var exception = await Assert.ThrowsAsync<GameCommandException>(() => connect);
        Assert.Equal(409, exception.StatusCode);
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

    [Fact]
    public async Task ConnectedGameReturnsToReadyWhenItLeavesTheServer()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        Assert.Equal(GameState.Connected, coordinator.Status.State);

        runtime.DisconnectReason = "Timed out";
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Null(coordinator.Status.Server);
        Assert.Equal("Disconnected from server:25565: Timed out", coordinator.Status.Message);
        var chat = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.SendChatAsync(new("hello"), CancellationToken.None));
        Assert.Equal(409, chat.StatusCode);

        runtime.DisconnectReason = null;
        var reconnect = await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        Assert.Equal("server", reconnect.Server.Host);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        Assert.Equal(2, runtime.ConnectCount);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectedGameDisconnectsOnRequestAndConnectsAgain()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        var status = await coordinator.DisconnectAsync(CancellationToken.None);

        Assert.Equal(1, runtime.DisconnectCount);
        Assert.Equal(GameState.Ready, status.State);
        Assert.Null(status.Server);
        Assert.Equal(OperationState.Succeeded, status.OperationState);
        Assert.Equal("client.disconnect.requested", status.Disconnect?.Code);
        Assert.Equal("Disconnected from server:25565", status.Message);
        var chat = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.SendChatAsync(new("hello"), CancellationToken.None));
        Assert.Equal(409, chat.StatusCode);

        var reconnect = await coordinator.ConnectAsync(new("other-server", 25565), CancellationToken.None);

        Assert.Equal("other-server", reconnect.Server.Host);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        Assert.Null(coordinator.Status.Disconnect);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisconnectRequiresAConnectedGame()
    {
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);

        var withoutGame = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.DisconnectAsync(CancellationToken.None));
        Assert.Equal(409, withoutGame.StatusCode);

        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var withoutServer = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.DisconnectAsync(CancellationToken.None));
        Assert.Equal(409, withoutServer.StatusCode);
        Assert.Equal(0, runtime.DisconnectCount);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RequestedDisconnectIsNotReportedAsALostConnection()
    {
        var runtime = new FakeGameRuntime { BlockDisconnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        var disconnect = coordinator.DisconnectAsync(CancellationToken.None);
        await WaitForOperationAsync(coordinator, "disconnect");

        // The game reports the closed connection while it leaves, which the connection monitor no longer reads.
        runtime.DisconnectReason = "Disconnected";
        int disconnectReads = runtime.DisconnectReads;
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        Assert.Equal(disconnectReads, runtime.DisconnectReads);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        var operation = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.DisconnectAsync(CancellationToken.None));
        Assert.Equal(409, operation.StatusCode);

        runtime.CompleteDisconnect();
        var status = await disconnect;

        Assert.Equal(GameState.Ready, status.State);
        Assert.Equal("client.disconnect.requested", status.Disconnect?.Code);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailedDisconnectLeavesTheGameConnectedAndWatched()
    {
        var runtime = new FakeGameRuntime { BlockDisconnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        var disconnect = coordinator.DisconnectAsync(CancellationToken.None);
        await WaitForOperationAsync(coordinator, "disconnect");
        runtime.FailDisconnect(new GameClientException("client.disconnect.failed", "disconnect", "disconnect.failed", "The connection to the server could not be closed"));
        var failure = await Assert.ThrowsAsync<GameClientException>(() => disconnect);

        Assert.Equal("client.disconnect.failed", failure.Failure.Code);
        Assert.Equal(GameState.Connected, coordinator.Status.State);
        Assert.Equal(OperationState.Failed, coordinator.Status.OperationState);

        // The game is watched again, so it still returns to ready once it leaves the server.
        runtime.DisconnectReason = "Timed out";
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Equal("client.connection.lost", coordinator.Status.Disconnect?.Code);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GameIsLoadingUntilItShowsAMenu()
    {
        // Issue #1608: the game was ready as soon as a window existed, such as NeoForge's loading window, minutes before its menu.
        var runtime = new FakeGameRuntime { Menu = null };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Loading);

        Assert.Equal("Game window is open", coordinator.Status.Message);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);

        // Screenshots and players work while the game loads, while chat needs a connected game.
        await coordinator.CaptureScreenshotAsync(CancellationToken.None);
        await coordinator.GetPlayersAsync(CancellationToken.None);
        var chat = await Assert.ThrowsAsync<GameCommandException>(() => coordinator.SendChatAsync(new("hello"), CancellationToken.None));
        Assert.Equal(409, chat.StatusCode);
        Assert.Equal(GameState.Loading, coordinator.Status.State);

        runtime.Menu = new("TitleScreen", Problem: null);
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Equal("Game menu is ready", coordinator.Status.Message);
        Assert.Equal("screenshot", coordinator.Status.Operation);
        Assert.Empty(coordinator.Status.Warnings);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectStartedWhileTheGameLoadsKeepsDescribingItself()
    {
        var runtime = new FakeGameRuntime { Menu = null, BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Loading);

        var connect = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        runtime.Menu = new("TitleScreen", Problem: null);
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Equal("connect running", coordinator.Status.Message);

        runtime.CompleteConnect();
        await connect;

        Assert.Equal(GameState.Connected, coordinator.Status.State);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GameThatConnectsBeforeItsMenuIsReportedStaysConnected()
    {
        var runtime = new FakeGameRuntime { Menu = null };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Loading);

        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        runtime.Menu = new("TitleScreen", Problem: null);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        Assert.Equal(GameState.Connected, coordinator.Status.State);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GameTheAgentCannotFollowIsReadyWithAWarning()
    {
        const string problem = "The client can't follow the game's screens: No unique chat screen submission flow was found";
        var runtime = new FakeGameRuntime { Menu = new(Screen: null, problem) };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Equal([problem], coordinator.Status.Warnings);

        await coordinator.StopGameAsync(CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReasonTheGameIsNotConnectedLastsUntilItConnects()
    {
        // Issue #1608: the next operation cleared a failed connect's reason, and a lost connection only had it in the message.
        var runtime = new FakeGameRuntime { BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        var rejected = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        runtime.FailConnect(new GameClientException("client.connect.rejected", "connect", "connection.rejected", "You are not white-listed on this server!"));
        await Assert.ThrowsAsync<GameClientException>(() => rejected);

        Assert.Equal(new ServerAddress("server", 25565), coordinator.Status.Disconnect?.Server);
        Assert.Equal("client.connect.rejected", coordinator.Status.Disconnect?.Code);
        Assert.Equal("connection.rejected", coordinator.Status.Disconnect?.Stage);
        Assert.Equal("You are not white-listed on this server!", coordinator.Status.Disconnect?.Message);

        await coordinator.CaptureScreenshotAsync(CancellationToken.None);

        Assert.Null(coordinator.Status.Failure);
        Assert.Equal("client.connect.rejected", coordinator.Status.Disconnect?.Code);

        var connect = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");

        Assert.Equal("client.connect.rejected", coordinator.Status.Disconnect?.Code);

        runtime.CompleteConnect();
        await connect;

        Assert.Null(coordinator.Status.Disconnect);

        runtime.DisconnectReason = "Timed out";
        await WaitForStateAsync(coordinator, GameState.Ready);

        Assert.Equal("client.connection.lost", coordinator.Status.Disconnect?.Code);
        Assert.Equal("connection.closed", coordinator.Status.Disconnect?.Stage);
        Assert.Equal("Timed out", coordinator.Status.Disconnect?.Message);

        await coordinator.StopGameAsync(CancellationToken.None);

        Assert.Null(coordinator.Status.Disconnect);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GameThatExitsWhileLoadingFailsAndStaysFailed()
    {
        var runtime = new FakeGameRuntime { Menu = null, BlockConnect = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Loading);

        var rejected = coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);
        await WaitForOperationAsync(coordinator, "connect");
        runtime.FailConnect(new GameClientException("client.connect.rejected", "connect", "connection.rejected", "The server is full!"));
        await Assert.ThrowsAsync<GameClientException>(() => rejected);

        Assert.Equal("client.connect.rejected", coordinator.Status.Disconnect?.Code);

        runtime.ExitGame(0, false);
        await WaitForStateAsync(coordinator, GameState.Failed);

        Assert.Null(coordinator.Status.Disconnect);
        Assert.Equal("client.process.exited", coordinator.Status.Failure?.Code);

        runtime.Menu = new("TitleScreen", Problem: null);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        Assert.Equal(GameState.Failed, coordinator.Status.State);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnansweredConnectionChecksKeepTheGameConnected()
    {
        var runtime = new FakeGameRuntime { FailDisconnectReads = true };
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.21.11", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);
        await coordinator.ConnectAsync(new("server", 25565), CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (runtime.DisconnectReads < 2)
            await Task.Delay(10, timeout.Token);

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

        // The exit code is the launcher's, so the message doesn't present it as Minecraft's.
        Assert.DoesNotContain("exit code", exception.Message);

        if (wasOutOfMemoryKilled)
            Assert.Contains("configured maximum heap of 2048 MiB", exception.Message);
        else
            Assert.Equal("Minecraft exited without a stop request", exception.Message);

        await WaitForStateAsync(coordinator, GameState.Failed);
        Assert.Equal(GameState.Failed, coordinator.Status.State);
        Assert.Equal(exitCode, coordinator.Status.ExitCode);
        Assert.Equal(expectedCode, coordinator.Status.Failure?.Code);

        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExitWithoutStopRequestIsAFailureThatLeavesTheLastOperationAlone()
    {
        // Issue #1611: the launcher exits 0 whether the game quit, crashed or was killed, so an exit without stop was reported as idle.
        var runtime = new FakeGameRuntime();
        using var coordinator = new GameCoordinator(runtime, NullLogger<GameCoordinator>.Instance);
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.StartVanillaAsync(new("1.17", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        runtime.ExitGame(0, false);

        await WaitForStateAsync(coordinator, GameState.Failed);
        Assert.Equal("start-vanilla", coordinator.Status.Operation);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);
        Assert.Equal(0, coordinator.Status.ExitCode);
        Assert.Null(coordinator.Status.ProcessId);
        Assert.Equal("client.process.exited", coordinator.Status.Failure?.Code);
        Assert.Equal("Minecraft exited without a stop request", coordinator.Status.Error);

        // Issue #1620: a game that exits without a crash report is described by the error it logged just before.
        await coordinator.StartVanillaAsync(new("1.17", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        runtime.ExitGame(0, false, lastError: "The game failed to start because the currently active LWJGL version is not compatible.");

        await WaitForStateAsync(coordinator, GameState.Failed);
        Assert.Equal("Game exited without a stop request", coordinator.Status.Message);
        Assert.Equal("Minecraft exited without a stop request, last error: The game failed to start because the currently active LWJGL version is not compatible.", coordinator.Status.Error);
        Assert.Equal("client.process.exited", coordinator.Status.Failure?.Code);

        // A failed game can be started again, and its crash report tells why it crashed.
        await coordinator.StartVanillaAsync(new("1.17", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        // The out-of-memory count covers the whole container, while a crash report proves the game ended on its own.
        const string crashCause = "Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash";
        runtime.ExitGame(0, wasOutOfMemoryKilled: true, crashCause);

        await WaitForStateAsync(coordinator, GameState.Failed);
        Assert.Equal("Game crashed", coordinator.Status.Message);
        Assert.Equal(crashCause, coordinator.Status.Error);
        Assert.Equal("client.process.crashed", coordinator.Status.Failure?.Code);
        Assert.Equal("crash-report", coordinator.Status.Failure?.Stage);
        Assert.Equal(OperationState.Succeeded, coordinator.Status.OperationState);

        await coordinator.StartVanillaAsync(new("1.17", []), CancellationToken.None);
        runtime.CompleteLaunch();
        await WaitForStateAsync(coordinator, GameState.Ready);

        await coordinator.StopGameAsync(CancellationToken.None);
        Assert.Equal(GameState.Idle, coordinator.Status.State);
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

    private static async Task WaitForWarningsAsync(GameCoordinator coordinator, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (coordinator.Status.Warnings.Count < count)
            await Task.Delay(10, timeout.Token);
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
        private TaskCompletionSource? _disconnect;
        private FakeManagedProcess? _process;
        private int _disconnectReads;

        public string? DisconnectReason { get; set; }

        /// <summary>What the game reports about its menu, or <see langword="null"/> while it loads. Games show their title screen by default.</summary>
        public GameMenu? Menu { get; set; } = new("TitleScreen", Problem: null);

        /// <summary>The container's memory limit in MiB. Containers have none by default.</summary>
        public long? MemoryLimitMb { get; init; }
        public bool FailDisconnectReads { get; init; }
        public int DisconnectReads => Volatile.Read(ref _disconnectReads);
        public bool BlockStop { get; init; }
        public bool BlockConnect { get; init; }
        public bool BlockDisconnect { get; init; }
        public bool FailScreenshot { get; init; }
        public int LaunchCount { get; private set; }
        public int StopCount { get; private set; }
        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public bool ConnectCancellationRequested { get; private set; }
        public int? LastMemoryMb { get; private set; }
        public string? LastVanillaVersion { get; private set; }
        public string? LastNeoForgeVersion { get; private set; }
        public string? LastNeoForgeMaximumMinecraftVersion { get; private set; }
        public string? LastNeoForgeLoaderVersion { get; private set; }
        public (string Loader, string Version, string? LoaderVersion)? LastModLoaderLaunch { get; private set; }
        public RunningGame? LastConnectGame { get; private set; }
        public Channel<string> Warnings { get; private set; } = Channel.CreateUnbounded<string>();

        public void FailLaunch(Exception exception) => _launch.SetException(exception);

        public void CompleteLaunch()
        {
            _process = new FakeManagedProcess(LaunchCount, LastMemoryMb);
            Warnings = Channel.CreateUnbounded<string>();
            _launch.SetResult(new(_process, $"test:{LaunchCount}", DateTimeOffset.UtcNow, new("test.port", "token", null), Warnings.Reader));
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

        public void CompleteDisconnect()
        {
            _disconnect?.SetResult();
        }

        public void FailDisconnect(Exception exception)
        {
            _disconnect?.SetException(exception);
        }

        public void ExitGame(int exitCode, bool wasOutOfMemoryKilled, string? crashCause = null, string? lastError = null)
        {
            _process?.Exit(exitCode, wasOutOfMemoryKilled, crashCause, lastError);
        }

        public Task WriteOptionsAsync(string options, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
        {
            LastVanillaVersion = version;
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task<RunningGame> LaunchNeoForgeAsync(
            string version,
            string? loaderVersion,
            string? maximumMinecraftVersion,
            IReadOnlyList<string> arguments,
            int? memoryMb,
            CancellationToken cancellationToken
        )
        {
            LastNeoForgeVersion = version;
            LastNeoForgeLoaderVersion = loaderVersion;
            LastNeoForgeMaximumMinecraftVersion = maximumMinecraftVersion;
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken)
        {
            LastMemoryMb = memoryMb;
            return BeginLaunch(cancellationToken);
        }

        public Task<RunningGame> LaunchModLoaderAsync(
            string loader,
            string version,
            string? loaderVersion,
            IReadOnlyList<string> arguments,
            int? memoryMb,
            CancellationToken cancellationToken
        )
        {
            LastModLoaderLaunch = (loader, version, loaderVersion);
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

        public Task DisconnectAsync(RunningGame game, CancellationToken cancellationToken)
        {
            DisconnectCount++;

            if (!BlockDisconnect)
                return Task.CompletedTask;

            _disconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => _disconnect.TrySetCanceled(cancellationToken));
            return _disconnect.Task;
        }

        public Task SendChatAsync(RunningGame game, string message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken) => FailScreenshot
            ? Task.FromException<byte[]>(new InvalidOperationException("Screenshot unavailable"))
            : Task.FromResult(Array.Empty<byte>());

        public Task<string?> ReadDisconnectReasonAsync(RunningGame game, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _disconnectReads);

            return FailDisconnectReads
                ? Task.FromException<string?>(new InvalidOperationException("The Minecraft agent did not report the connection"))
                : Task.FromResult(DisconnectReason);
        }

        public string GetGameDirectory() => "/root/.minecraft";

        public string GetModpackDirectory() => "/root/.minecraft/modpack";

        public long? ReadMemoryLimitMb() => MemoryLimitMb;

        public Task<GameMenu?> ReadMenuAsync(RunningGame game, CancellationToken cancellationToken) => Task.FromResult(Menu);

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
        public string? CrashCause { get; private set; }
        public string? LastError { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

        public void KillTree() => Exit(137, false);

        public void Exit(int exitCode, bool wasOutOfMemoryKilled = false, string? crashCause = null, string? lastError = null)
        {
            HasExited = true;
            ExitCode = exitCode;
            WasOutOfMemoryKilled = wasOutOfMemoryKilled;
            CrashCause = crashCause;
            LastError = lastError;
            _exit.TrySetResult();
        }

        public void Dispose()
        {
        }
    }
}
