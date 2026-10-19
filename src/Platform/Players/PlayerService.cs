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

namespace Void.Proxy.Players;

public class PlayerService(ILogger<PlayerService> logger, IDependencyService dependencies, ILinkService links, IEventService events, ISettings settings) : IPlayerService, IEventListener
{
    private readonly AsyncLock _lock = new();
    private readonly List<PlayerProxy> _players = [];

    public IEnumerable<IPlayer> All => _players.Select(proxy => proxy.Source);

    public void ForEach(Action<IPlayer> action)
    {
        for (var i = _players.Count - 1; i >= 0; i--)
            action(_players[i].Unwrap());
    }

    public async ValueTask ForEachAsync(Func<IPlayer, CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        for (var i = _players.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await action(_players[i].Unwrap(), cancellationToken);
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

            await EndAsync(player, "Failed to find a server for you", cancellationToken);
        }
    }

    public async ValueTask UpgradeAsync(IPlayer player, IPlayer upgradedPlayer, CancellationToken cancellationToken)
    {
        using var sync = await _lock.LockAsync(cancellationToken);

        var proxy = _players.FirstOrDefault(proxy => proxy == player || proxy.Source == player) ??
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

            if (kicked)
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
        }
        catch (StreamException)
        {
            // The player has already left, so there is nobody to tell why.
        }
        finally
        {
            channel.Close();
            player.Client.Close();
        }

        if (!HasLiveLink(player))
            await RaiseDisconnectedOnceAsync(player, cancellationToken);

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
            await RaiseDisconnectedOnceAsync(@event.Player, cancellationToken);
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

            await EndAsync(@event.Player, "Failed to find a next server for you", cancellationToken);
        }
    }

    [Subscribe(PostOrder.Last)]
    public async ValueTask OnPlayerDisconnected(PlayerDisconnectedEvent @event, CancellationToken cancellationToken)
    {
        await RemovePlayerAsync(@event.Player, cancellationToken);
    }

    /// <summary>
    /// Ends a connection attempt that failed. A player that is still connected is kicked, and one that has already left is
    /// disconnected, so the player never stays in the list, where its name would still count as online.
    /// </summary>
    private async ValueTask EndAsync(IPlayer player, string kickText, CancellationToken cancellationToken)
    {
        player = player.Unwrap();

        if (HasLiveLink(player))
            return;

        // Kicking a player without a channel would first try to build one, which waits for bytes a closed client never sends.
        if (player.Context.Channel is { IsAlive: true })
        {
            try
            {
                await KickPlayerAsync(player, kickText, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Player {Player} could not be kicked after a failed connection", player);
            }
        }
        else
        {
            player.Context.Channel?.Close();
            player.Client.Close();
        }

        await RaiseDisconnectedOnceAsync(player, cancellationToken);
    }

    private bool HasLiveLink(IPlayer player)
    {
        return links.TryGetLink(player, out var link) && link.IsAlive;
    }

    /// <summary>
    /// Tells listeners that the player has disconnected, once per player. A kick and a link that stops can both end the same player.
    /// </summary>
    private async ValueTask RaiseDisconnectedOnceAsync(IPlayer player, CancellationToken cancellationToken)
    {
        player = player.Unwrap();

        using (await _lock.LockAsync(cancellationToken))
        {
            var proxy = _players.FirstOrDefault(proxy => proxy.Source == player);

            if (proxy is null || proxy.IsDisconnecting)
                return;

            proxy.IsDisconnecting = true;
        }

        try
        {
            await events.ThrowAsync(new PlayerDisconnectedEvent(player), cancellationToken);
        }
        finally
        {
            // A listener that throws would otherwise skip the removal and leave the player in the list.
            await RemovePlayerAsync(player, CancellationToken.None);
        }
    }

    private async ValueTask RemovePlayerAsync(IPlayer player, CancellationToken cancellationToken)
    {
        using (var sync = await _lock.LockAsync(cancellationToken))
        {
            var count = _players.RemoveAll(proxy => proxy == player || proxy.Source == player);

            if (count is 0)
                return;

            if (count > 1)
                logger.LogWarning("Multiple instances of same player disconnected at once");
        }

        logger.LogInformation("Player {Player} disconnected", player);

        dependencies.DisposePlayerScope(player.Context);
        await player.DisposeAsync();
    }
}
