using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// A client container and the game it runs, which the tests of a collection play one after another. The game launches with the first test
/// that plays it, and again after a test that didn't end left it stopped.
/// </summary>
/// <param name="setup">The modded game to play, or <see langword="null"/> to play vanilla Minecraft of the protocol version.</param>
public sealed class ReusableGame(ProtocolVersion protocolVersion, GameSetup? setup) : IIntegrationSide
{
    private PortableMinecraftClient? _client;
    private PortableMinecraftClient.Game? _game;
    private GameSession? _session;

    public string LogFileName => "client.log";
    public IEnumerable<string> Logs => _client?.Logs ?? [];

    // Forge for Minecraft 1.7.2 can't start on the client's Java without LegacyForgePatcher.
    private IReadOnlyList<Uri> JvmAgents => setup is { } moddedSetup && LegacyForgePatcher.AppliesTo(moddedSetup.Loader, moddedSetup.MinecraftRelease) ? [LegacyForgePatcher.Url] : [];

    public void ClearLogs()
    {
        _client?.ClearLogs();
    }

    public async Task<IEnumerable<string>> ReadLogsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        return _client is { } client ? await client.ReadLogsAsync(since, cancellationToken) : [];
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Begins a test on the game, launching it when it isn't running.</summary>
    /// <param name="logSides">The sides whose logs the test writes into its steps, besides the client's.</param>
    public async Task<GameSession> BeginSessionAsync(string testName, IEnumerable<IIntegrationSide> logSides, CancellationToken cancellationToken = default)
    {
        if (_session is not null)
            throw new InvalidOperationException($"{testName} can't play a game another test is still playing");

        _client ??= await PortableMinecraftClient.CreateAsync(JvmAgents, cancellationToken);

        // A game that crashed, or that its client stopped, since the last test launches again.
        if (_game is { } game && !await IsPlayableAsync(game, cancellationToken))
            await StopGameAsync();

        try
        {
            _game ??= await LaunchGameAsync(_client, cancellationToken);
        }
        catch
        {
            // The next test starts another client rather than one that failed to launch its game.
            await ReleaseAsync();
            throw;
        }

        return _session = new GameSession(this, _game, testName, [this, .. logSides]);
    }

    // A Forge game can corrupt its own config on its first launch and hang, so a game that failed to launch gets one more try in a new client.
    private async Task<PortableMinecraftClient.Game> LaunchGameAsync(PortableMinecraftClient client, CancellationToken cancellationToken)
    {
        try
        {
            return await client.LaunchGameAsync(protocolVersion, setup, cancellationToken);
        }
        catch (IntegrationTestException exception)
        {
            await PortableMinecraftClient.Game.LogAsync($"Launching the game again in a new client: {exception.Message}", CancellationToken.None);
            await ReleaseAsync();

            _client = await PortableMinecraftClient.CreateAsync(JvmAgents, cancellationToken);
            return await _client.LaunchGameAsync(protocolVersion, setup, cancellationToken);
        }
    }

    /// <summary>Stops the game and its client, which a collection no longer needs.</summary>
    public async Task ReleaseAsync()
    {
        await StopGameAsync();

        if (_client is not { } client)
            return;

        _client = null;
        await client.DisposeAsync();
    }

    internal async Task EndSessionAsync(GameSession session, bool discardGame)
    {
        if (_session != session)
            return;

        _session = null;

        if (discardGame)
            await StopGameAsync();
    }

    private static async Task<bool> IsPlayableAsync(PortableMinecraftClient.Game game, CancellationToken cancellationToken)
    {
        try
        {
            return await game.IsPlayableAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await PortableMinecraftClient.Game.LogAsync($"Reading whether {game.GameName} can play failed: {exception.Message}", CancellationToken.None);
            return false;
        }
    }

    private async Task StopGameAsync()
    {
        if (_game is not { } game)
            return;

        _game = null;

        try
        {
            await game.StopAsync(Timeouts.StepTimeoutToken);
        }
        catch (Exception exception)
        {
            await PortableMinecraftClient.Game.LogAsync($"Stopping {game.GameName} failed: {exception.Message}", CancellationToken.None);
        }
    }
}
