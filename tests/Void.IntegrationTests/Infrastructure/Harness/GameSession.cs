using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// One test played on a reused game. It writes what the test did into the test's steps, and leaves the game at its menu for the next test,
/// or stops it when the test didn't end, so a game left in an unknown state can't fail the next test.
/// </summary>
public sealed class GameSession(ReusableGame owner, PortableMinecraftClient.Game game, string testName, IReadOnlyList<IIntegrationSide> logSides) : IAsyncDisposable
{
    private readonly string _workingDirectory = PortableMinecraftClient.Game.StepsDirectory(testName, game.ProtocolVersion, game.Setup, game.Username);
    private bool _ended;
    private int _step;

    public string Username => game.Username;
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_ended)
                await TryMakeStepAsync("exit", Timeouts.StepTimeoutToken);
        }
        finally
        {
            await TryWriteLogsAsync(Timeouts.StepTimeoutToken);
            await owner.EndSessionAsync(this, discardGame: !_ended);
        }
    }

    public async Task JoinServerAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        await game.JoinServerAsync(endPoint, cancellationToken);
    }

    /// <summary>Waits until the server the game is on has kicked it, and checks that the game shows the reason the server gave.</summary>
    public async Task ExpectKickAsync(string reason, CancellationToken cancellationToken = default)
    {
        var shownReason = await game.WaitForLeaveAsync(cancellationToken);
        await MakeStepAsync("kicked", cancellationToken);

        if (shownReason?.Contains(reason, StringComparison.Ordinal) is not true)
            throw new IntegrationTestException($"{game.GameName} left its server showing {JsonSerializer.Serialize(shownReason)} rather than the server's reason {reason}");
    }

    public async Task JoinServerExpectingFailureAsync(EndPoint endPoint, string expectedReason, CancellationToken cancellationToken = default)
    {
        await game.JoinServerExpectingFailureAsync(endPoint, expectedReason, cancellationToken);
    }

    public async Task SendTextMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        await SendTextMessagesAsync([text], cancellationToken);
    }

    public async Task SendTextMessagesAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        foreach (var text in texts)
        {
            // Is it a Chat Command?
            var expectTask = text.StartsWith('/')
                ? Task.Delay(3_000, cancellationToken) // Give some room for client-server reaction
                : game.Container.ExpectTextAsync(text, cancellationToken); // Expect the text to appear in logs

            await game.SendChatAsync(text, cancellationToken);
            await expectTask;

            await MakeStepAsync("chat", cancellationToken);
        }
    }

    public async Task EnsureStableAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStableAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    public async Task EnsureStableAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        await game.WaitForLogsSilenceAsync(duration, cancellationToken);
        await MakeStepAsync("stable", cancellationToken);
    }

    public async Task<PortableMinecraftClient.Game.ApiGamePlayers?> TryReadPlayersAsync(CancellationToken cancellationToken = default)
    {
        return await game.TryReadPlayersAsync(cancellationToken);
    }

    public async Task<string> ReadDiagnosticsAsync()
    {
        return await game.ReadDiagnosticsAsync();
    }

    /// <summary>
    /// Ends the test by leaving the server the game is on, and waits until that server has seen the player leave, so the next test can join
    /// as the same player. A game that is no longer on that server, such as one that crashed or was kicked, fails the test.
    /// </summary>
    /// <param name="joinedServer">The server the player is on, behind the proxy when it joined through one, or <see langword="null"/> when it joined none.</param>
    public async Task EndAsync(MinecraftServer? joinedServer, CancellationToken cancellationToken = default)
    {
        if (await game.IsConnectedAsync(cancellationToken))
        {
            var disconnectedAt = DateTime.UtcNow;
            await game.DisconnectAsync(cancellationToken);

            if (joinedServer is not null)
                await joinedServer.Container.ExpectTextAsync($"{Username} left the game", disconnectedAt, cancellationToken);
        }
        else if (joinedServer is not null)
        {
            throw new IntegrationTestException($"Game of {Username} was no longer on its server when the test ended. Client diagnostics:\n{await game.ReadDiagnosticsAsync()}");
        }

        await MakeStepAsync("disconnected", cancellationToken);
        _ended = true;
    }

    private async Task MakeStepAsync(string action, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_workingDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_workingDirectory, $"step-{++_step}-{action}.png"), await game.TakeScreenshotAsync(cancellationToken), cancellationToken);
    }

    private async Task TryMakeStepAsync(string action, CancellationToken cancellationToken = default)
    {
        try
        {
            await MakeStepAsync(action, cancellationToken);
        }
        catch (Exception exception)
        {
            await PortableMinecraftClient.Game.LogAsync($"Failed to write {action} step: {exception.Message}", CancellationToken.None);
        }
    }

    private async Task TryWriteLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_workingDirectory);

            foreach (var side in logSides)
            {
                var logs = await side.ReadLogsAsync(StartedAt, cancellationToken);
                await File.WriteAllLinesAsync(Path.Combine(_workingDirectory, side.LogFileName), logs, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            await PortableMinecraftClient.Game.LogAsync($"Failed to write step logs: {exception.Message}", CancellationToken.None);
        }
    }
}
