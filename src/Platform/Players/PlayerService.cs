using System.Diagnostics;
using System.Net.Sockets;
using Nito.AsyncEx;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Settings;
using Void.Proxy.Players.Extensions;
using Void.Proxy.Plugins.Containers;

namespace Void.Proxy.Players;

public class PlayerService(ILogger<PlayerService> logger, IDependencyService dependencies, ILinkService links, IEventService events, ISettings settings) : IPlayerService, IEventListener
{
    private readonly AsyncLock _lock = new();
    // Players connect and leave while others enumerate them, as plugins that load do, so each enumeration reads a snapshot
    private readonly CopyOnWriteCollection<PlayerProxy> _players = new();

    public IEnumerable<IPlayer> All => _players.Snapshot.Select(proxy => proxy.Source);

    public void ForEach(Action<IPlayer> action)
    {
        var players = _players.Snapshot;

        for (var i = players.Length - 1; i >= 0; i--)
            action(players[i].Unwrap());
    }

    public async ValueTask ForEachAsync(Func<IPlayer, CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        var players = _players.Snapshot;

        for (var i = players.Length - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await action(players[i].Unwrap(), cancellationToken);
        }
    }

    public async ValueTask AcceptPlayerAsync(TcpClient client, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Accepted client from {RemoteEndPoint}", client.Client.RemoteEndPoint);

        var player = new PlayerProxy(await events.ThrowWithResultAsync(new PlayerConnectingEvent(client, player => dependencies.GetEntryPoint(player)), cancellationToken) ??
                                     throw new InvalidOperationException("Player is not instantiated"));

        dependencies.ActivatePlayerScope(player.Context);

        try
        {
            using (var sync = await _lock.LockAsync(cancellationToken))
                _players.Add(player);

            logger.LogTrace("Player {Player} connecting", player);
            var result = await links.ConnectAnywhereAsync(player, cancellationToken);

            if (result is ConnectionResult.NotConnected)
                await player.KickAsync("Failed to find a server for you", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (exception is not StreamException)
                logger.LogError(exception, "Client {RemoteEndPoint} cannot be proxied", player.RemoteEndPoint);

            if (player.Context.Channel is { IsAlive: true })
            {
                await KickPlayerAsync(player, "Failed to find a server for you", cancellationToken);
            }
            else
            {
                // Disposing the player closes its channel, but a player that failed before getting one would keep its connection open
                player.Client.Close();
                await DisconnectAsync(player, cancellationToken);
            }
        }
    }

    public async ValueTask UpgradeAsync(IPlayer player, IPlayer upgradedPlayer, CancellationToken cancellationToken)
    {
        using var sync = await _lock.LockAsync(cancellationToken);

        var proxy = _players.Snapshot.FirstOrDefault(proxy => proxy == player || proxy.Source == player) ??
                    throw new InvalidOperationException($"Player {player} not found");

        proxy.Replace(upgradedPlayer);
    }

    public async ValueTask KickPlayerAsync(IPlayer player, string? text = null, CancellationToken cancellationToken = default)
    {
        player = player.Unwrap();
        await KickPlayerAsync(player, new PlayerKickEvent(player, text), cancellationToken);
    }

    public async ValueTask KickPlayerAsync(IPlayer player, PlayerKickEvent playerKickEvent, CancellationToken cancellationToken = default)
    {
        player = player.Unwrap();
        logger.LogTrace("Kicking player {Player}", player);

        if (!All.Contains(player))
            return;

        var channel = await player.GetChannelAsync(cancellationToken);
        channel.TryPause();

        try
        {
            var kicked = await events.ThrowWithResultAsync(playerKickEvent, cancellationToken);

            // Only a live link notices the player leaving, so a player that is still joining a server is read here until it leaves
            var linked = links.TryGetLink(player, out var playingLink) && playingLink.IsAlive;

            if (kicked && linked)
            {
                var timestamp = Stopwatch.GetTimestamp();

                while (channel.IsAlive)
                {
                    if (Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds > settings.KickTimeout)
                        break;

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await Task.Delay(100, cancellationToken);
                }

                if (channel.IsAlive)
                    logger.LogWarning("Plugins didn't handle graceful {Player} player kick in {Timeout}ms", player, settings.KickTimeout);
            }

            channel.TryResume();
            await channel.FlushAsync(cancellationToken);

            if (kicked && !linked)
                await ReadUntilLeftAsync(player, cancellationToken);
        }
        catch (StreamException)
        {
            // The player has already left, so there is nobody to tell why.
        }
        finally
        {
            channel.Close();
            player.Client.Close();

            // A live link announces the disconnect when it stops. A link that was stopped for a server switch can still be listed.
            if (!links.TryGetLink(player, out var link) || !link.IsAlive)
                await DisconnectAsync(player, cancellationToken);
        }

        logger.LogTrace("Player {Player} kicked", player);
    }

    [Subscribe]
    public async ValueTask OnLinkStopped(LinkStoppedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Reason is LinkStopReason.Requested)
            return;

        var isPlayerAlive = @event.Reason is not LinkStopReason.PlayerDisconnected && @event.Link.PlayerChannel.IsAlive;

        if (!isPlayerAlive)
        {
            await DisconnectAsync(@event.Player, cancellationToken);
            return;
        }

        logger.LogTrace("Reconnecting player {Player} after link {Link} stopped due to {Reason}", @event.Player, @event.Link, @event.Reason);

        try
        {
            var result = await links.ConnectAnywhereAsync(@event.Player, [@event.Link.Server], cancellationToken);

            if (result is ConnectionResult.NotConnected)
                await @event.Player.KickAsync("Failed to find a next server for you", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A side closing its connection is an ordinary way for the move to end, such as the player quitting meanwhile.
            if (exception is StreamException)
                logger.LogDebug("Player {Player} was not moved to another server because a connection closed", @event.Player);
            else
                logger.LogError(exception, "Player {Player} cannot be moved to another server", @event.Player);

            if (@event.Link.PlayerChannel.IsAlive)
                await KickPlayerAsync(@event.Player, "Failed to find a next server for you", cancellationToken);
            else
                await DisconnectAsync(@event.Player, cancellationToken);
        }
    }

    private async ValueTask DisconnectAsync(IPlayer player, CancellationToken cancellationToken)
    {
        player = player.Unwrap();

        // Whoever takes the player off the list announces it, so a kick racing a link stop can't announce it twice
        using (await _lock.LockAsync(cancellationToken))
        {
            var count = _players.RemoveAll(proxy => proxy.Source == player);

            if (count is 0)
                return;

            if (count > 1)
                logger.LogWarning("Multiple instances of same player disconnected at once");
        }

        try
        {
            await events.ThrowAsync(new PlayerDisconnectedEvent(player), cancellationToken);
        }
        finally
        {
            logger.LogInformation("Player {Player} disconnected", player);

            // A disposed player opens no scopes again, so none outlive the ones disposed here
            await player.DisposeAsync();
            dependencies.DisposePlayerScope(player.Context);
        }
    }

    // Closing a connection the player still sends to resets it, and the player may then show the reset instead of the reason it was kicked for
    private async ValueTask ReadUntilLeftAsync(IPlayer player, CancellationToken cancellationToken)
    {
        if (!player.Client.Connected)
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.KickTimeout);

        try
        {
            await player.Client.GetStream().CopyToAsync(Stream.Null, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogTrace("Player {Player} didn't leave in {Timeout}ms after its kick", player, settings.KickTimeout);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The player has already left.
        }
    }
}
