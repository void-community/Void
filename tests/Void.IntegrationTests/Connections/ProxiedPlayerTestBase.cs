using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class ProxiedPlayerTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private const double CoordinateTolerance = 1.0 / 32.0;
    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        try
        {
            await LoggedExecutorAsync(RunAttemptAsync, fixture.Game1, fixture.Game2, fixture.VoidProxy, fixture.Server1);
        }
        finally
        {
            // No other test plays the second game, so it stops rather than holding memory while the collection goes on.
            await fixture.Game2.ReleaseAsync();
        }
    }

    private async Task RunAttemptAsync()
    {
        using var disconnectionCancellation = new CancellationTokenSource();
        var disconnectionTasks = new List<Task>();

        try
        {
            await RunPlayersAsync(disconnectionCancellation.Token, disconnectionTasks);
            await Task.WhenAll(disconnectionTasks).WaitAsync(Timeouts.StepTimeout);
        }
        finally
        {
            await disconnectionCancellation.CancelAsync();
            await Task.WhenAll(disconnectionTasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task RunPlayersAsync(CancellationToken disconnectionCancellationToken, List<Task> disconnectionTasks)
    {
        await using var firstGame = await fixture.Game1.BeginSessionAsync(nameof(ProxiedPlayerTestBase), [fixture.VoidProxy, fixture.Server1], Timeouts.SetupTimeoutToken);
        await using var secondGame = await fixture.Game2.BeginSessionAsync(nameof(ProxiedPlayerTestBase), [fixture.VoidProxy, fixture.Server1], Timeouts.SetupTimeoutToken);

        disconnectionTasks.Add(fixture.VoidProxy.WaitForPlayerDisconnectionAsync(firstGame.Username, disconnectionCancellationToken));
        disconnectionTasks.Add(fixture.VoidProxy.WaitForPlayerDisconnectionAsync(secondGame.Username, disconnectionCancellationToken));

        await ConnectPlayerAsync(firstGame);
        await ConnectPlayerAsync(secondGame);

        // The first game played the collection's earlier tests, and its player comes back wherever it was, which can be out of the other's sight
        await fixture.Server1.RunCommandAsync($"tp {firstGame.Username} {secondGame.Username}", Timeouts.StepTimeoutToken);

        await WaitForReciprocalCoordinatesAsync(firstGame, secondGame);

        await Task.WhenAll(firstGame.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken), secondGame.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken));
    }

    private async Task ConnectPlayerAsync(GameSession game)
    {
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(Timeouts.StepTimeoutToken);

        try
        {
            await game.JoinServerAsync(ProxyEndPoint, connectionCancellation.Token);
        }
        catch (OperationCanceledException) when (connectionCancellation.IsCancellationRequested)
        {
            // Client-side player readiness is authoritative when visual confirmation exceeds its step timeout.
            Console.WriteLine($"Connection request detached; background client diagnostics:\n{await game.ReadDiagnosticsAsync()}");
        }

        await WaitForLocalPlayerAsync(game, Timeouts.StepTimeoutToken);
    }

    private static async Task WaitForLocalPlayerAsync(GameSession game, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var players = await game.TryReadPlayersAsync(cancellationToken);

                if (players?.Local.Name == game.Username)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timed out waiting for local player {game.Username}. Client diagnostics:\n{await game.ReadDiagnosticsAsync()}");
        }
    }

    private static async Task WaitForReciprocalCoordinatesAsync(GameSession firstGame, GameSession secondGame)
    {
        using var playersTimeout = CancellationTokenSource.CreateLinkedTokenSource(Timeouts.StepTimeoutToken);
        PortableMinecraftClient.Game.ApiGamePlayers? firstPlayers = null;
        PortableMinecraftClient.Game.ApiGamePlayers? secondPlayers = null;

        try
        {
            while (!playersTimeout.IsCancellationRequested)
            {
                var firstPlayersTask = firstGame.TryReadPlayersAsync(playersTimeout.Token);
                var secondPlayersTask = secondGame.TryReadPlayersAsync(playersTimeout.Token);
                await Task.WhenAll(firstPlayersTask, secondPlayersTask);
                firstPlayers = await firstPlayersTask;
                secondPlayers = await secondPlayersTask;

                var reciprocalPlayers = FindReciprocalPlayers(firstGame, secondGame, firstPlayers, secondPlayers);

                if (reciprocalPlayers is not null)
                {
                    Assert.Equal(firstGame.Username, reciprocalPlayers.First.Local.Name);
                    Assert.Equal(secondGame.Username, reciprocalPlayers.Second.Local.Name);
                    AssertPosition(reciprocalPlayers.Second.Local.Position, reciprocalPlayers.SecondAsSeenByFirst.Position);
                    AssertPosition(reciprocalPlayers.First.Local.Position, reciprocalPlayers.FirstAsSeenBySecond.Position);
                    AssertRotation(reciprocalPlayers.First.Local.Body, reciprocalPlayers.First.Local.Head);
                    AssertRotation(reciprocalPlayers.Second.Local.Body, reciprocalPlayers.Second.Local.Head);
                    AssertRotation(reciprocalPlayers.FirstAsSeenBySecond.Body, reciprocalPlayers.FirstAsSeenBySecond.Head);
                    AssertRotation(reciprocalPlayers.SecondAsSeenByFirst.Body, reciprocalPlayers.SecondAsSeenByFirst.Head);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), playersTimeout.Token);
            }

            playersTimeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (playersTimeout.IsCancellationRequested)
        {
            var firstDiagnostics = await firstGame.ReadDiagnosticsAsync();
            var secondDiagnostics = await secondGame.ReadDiagnosticsAsync();
            throw new InvalidOperationException($"Timed out waiting for reciprocal coordinates. First players: {JsonSerializer.Serialize(firstPlayers)}; second players: {JsonSerializer.Serialize(secondPlayers)}; first diagnostics:\n{firstDiagnostics}\nsecond diagnostics:\n{secondDiagnostics}");
        }
    }

    private static ReciprocalPlayers? FindReciprocalPlayers(GameSession firstGame, GameSession secondGame,PortableMinecraftClient.Game.ApiGamePlayers? firstPlayers, PortableMinecraftClient.Game.ApiGamePlayers? secondPlayers)
    {
        var secondAsSeenByFirst = firstPlayers?.Remote.SingleOrDefault(player => player.Name == secondGame.Username);
        var firstAsSeenBySecond = secondPlayers?.Remote.SingleOrDefault(player => player.Name == firstGame.Username);

        if (firstPlayers is null || secondPlayers is null || secondAsSeenByFirst is null || firstAsSeenBySecond is null)
            return null;

        return PositionsMatch(secondPlayers.Local.Position, secondAsSeenByFirst.Position) &&
               PositionsMatch(firstPlayers.Local.Position, firstAsSeenBySecond.Position)
            ? new(firstPlayers, secondPlayers, secondAsSeenByFirst, firstAsSeenBySecond)
            : null;
    }

    private static void AssertPosition(PortableMinecraftClient.Game.ApiPosition expected, PortableMinecraftClient.Game.ApiPosition actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, CoordinateTolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, CoordinateTolerance);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, CoordinateTolerance);
    }

    private static bool PositionsMatch(PortableMinecraftClient.Game.ApiPosition expected, PortableMinecraftClient.Game.ApiPosition actual)
    {
        return Math.Abs(expected.X - actual.X) <= CoordinateTolerance &&
               Math.Abs(expected.Y - actual.Y) <= CoordinateTolerance &&
               Math.Abs(expected.Z - actual.Z) <= CoordinateTolerance;
    }

    private static void AssertRotation(PortableMinecraftClient.Game.ApiBodyRotation body, PortableMinecraftClient.Game.ApiHeadRotation head)
    {
        Assert.True(double.IsFinite(body.Yaw));
        Assert.True(double.IsFinite(head.Yaw));
        Assert.True(double.IsFinite(head.Pitch));
    }

    private sealed record ReciprocalPlayers(PortableMinecraftClient.Game.ApiGamePlayers First, PortableMinecraftClient.Game.ApiGamePlayers Second, PortableMinecraftClient.Game.ApiRemoteGamePlayer SecondAsSeenByFirst, PortableMinecraftClient.Game.ApiRemoteGamePlayer FirstAsSeenBySecond);
}
