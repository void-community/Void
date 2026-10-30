using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Nito.AsyncEx;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Servers;
using Void.Proxy.Players.Extensions;
using Void.Proxy.Plugins.Containers;

namespace Void.Proxy.Links;

public class LinkService(ILogger<LinkService> logger, IServerService servers, IEventService events, IHostApplicationLifetime hostApplicationLifetime) : ILinkService, IEventListener
{
    // Links start and stop while players look theirs up, so each lookup reads a snapshot
    private readonly CopyOnWriteCollection<ILink> _activeLinks = new();
    private readonly AsyncLock _lock = new();
    private readonly CopyOnWriteCollection<ILink> _weakLinks = new();

    public IReadOnlyList<ILink> All => _activeLinks.Snapshot;

    public async ValueTask<ConnectionResult> ConnectAsync(IPlayer player, IServer server, CancellationToken cancellationToken = default)
    {
        return await ConnectOrKickAsync(player, server, anonymous: false, cancellationToken);
    }

    public async ValueTask<ConnectionResult> ConnectAnywhereAsync(IPlayer player, CancellationToken cancellationToken = default)
    {
        return await ConnectAnywhereAsync(player, [], cancellationToken);
    }

    public async ValueTask<ConnectionResult> ConnectAnywhereAsync(IPlayer player, IEnumerable<IServer> ignoredServers, CancellationToken cancellationToken = default)
    {
        logger.LogTrace("Looking for a server for {Player} player", player);

        var firstConnection = player.Context.Channel is null;
        _ = await player.GetChannelBuilderAsync(cancellationToken);

        // After searching for player channel builder, player might be upgraded to another implementation, unwrap proxy again
        player = player.Unwrap();

        var anonymous = false;

        if (!firstConnection)
            return await ConnectAnyAsync(player, ignoredServers, anonymous, cancellationToken);

        var playerConnectedEvent = new PlayerConnectedEvent(player);
        anonymous = await events.ThrowWithResultAsync(playerConnectedEvent, cancellationToken);

        var selectedServer = await events.ThrowWithResultAsync(new PlayerSearchServerEvent(player.Unwrap(), playerConnectedEvent.ConnectedWith), cancellationToken);

        return selectedServer is null
            ? await ConnectAnyAsync(player, ignoredServers, anonymous, cancellationToken)
            : await ConnectCoreAsync(player, selectedServer, anonymous, cancellationToken);

        async ValueTask<ConnectionResult> ConnectAnyAsync(IPlayer player, IEnumerable<IServer> ignoredServers, bool anonymous, CancellationToken cancellationToken)
        {
            var candidates = servers.All.Except(ignoredServers);
            var lastServer = candidates.LastOrDefault();

            if (lastServer is null)
            {
                await player.KickAsync("No available servers to connect you to.", cancellationToken);
                return ConnectionResult.NotConnected;
            }

            foreach (var server in candidates.Except([lastServer]))
            {
                try
                {
                    var result = await ConnectCoreAsync(player, server, anonymous, cancellationToken);

                    if (result is ConnectionResult.Connected)
                        return ConnectionResult.Connected;

                    // A player that a server kicked while joining has no other server to try
                    if (player.Context.Channel is not { IsAlive: true })
                        return result;
                }
                catch (StreamException) when (player.Context.Channel is { IsAlive: true })
                {
                    // Try next server, unless the player is the one who left
                    logger.LogWarning("Player {Player} could not join server {Server}", player, server);
                }
            }

            return await ConnectOrKickAsync(player, lastServer, anonymous, cancellationToken);
        }
    }

    public bool TryGetLink(IPlayer player, [NotNullWhen(true)] out ILink? link)
    {
        player = player.Unwrap();
        link = _activeLinks.Snapshot.FirstOrDefault(link => link.Player == player);
        return link is not null;
    }

    public bool TryGetWeakLink(IPlayer player, [NotNullWhen(true)] out ILink? link)
    {
        player = player.Unwrap();
        link = _weakLinks.Snapshot.FirstOrDefault(link => link.Player == player);
        return link is not null;
    }

    public bool HasLink(IPlayer player)
    {
        return _activeLinks.Snapshot.Any(link => link.Player == player);
    }

    [Subscribe(PostOrder.First)]
    public async ValueTask OnLinkStopped(LinkStoppedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Link.IsAlive)
            throw new Exception($"Link {@event.Link} is still alive");

        using (await _lock.LockAsync(cancellationToken))
        {
            if (_activeLinks.RemoveAll(link => link == @event.Link) is 0)
                throw new InvalidOperationException($"Link {@event.Link} is not found");

            _weakLinks.RemoveAll(link => link == @event.Link);
        }

        // IServer channel is no longer needed
        @event.Link.ServerChannel.Close();

        if (@event.Reason is not LinkStopReason.Requested)
        {
            if (!@event.Link.PlayerChannel.IsAlive || !await @event.Player.IsProtocolSupportedAsync(cancellationToken))
                @event.Link.PlayerChannel.Close();
        }

        logger.LogTrace("Stopped forwarding {Link} traffic", @event.Link);
    }

    private async ValueTask<ConnectionResult> ConnectOrKickAsync(IPlayer player, IServer server, bool anonymous = false, CancellationToken cancellationToken = default)
    {
        var previousServer = player.Server;
        var result = await ConnectOrFailAsync(server);

        if (result is ConnectionResult.Connected)
            return result;

        // A player that left or was kicked while joining has no server to go back to
        if (player.Context.Channel is not { IsAlive: true })
            return result;

        if (previousServer is null)
        {
            await player.KickAsync("Could not connect you to the target server.", cancellationToken);
            return result;
        }

        result = await ConnectOrFailAsync(previousServer);

        if (result is not ConnectionResult.NotConnected)
            return result;

        await player.KickAsync("Could not connect you to the target server nor back to the previous server.", cancellationToken);
        return result;

        async ValueTask<ConnectionResult> ConnectOrFailAsync(IServer server)
        {
            try
            {
                return await ConnectCoreAsync(player, server, anonymous, cancellationToken);
            }
            catch (StreamException) when (player.Context.Channel is { IsAlive: true })
            {
                // Otherwise the player would be left without any server
                logger.LogWarning("Player {Player} could not join server {Server}", player, server);
                return ConnectionResult.NotConnected;
            }
        }
    }

    private async ValueTask<ConnectionResult> ConnectCoreAsync(IPlayer player, IServer server, bool anonymous = false, CancellationToken cancellationToken = default)
    {
        var playerChannel = await player.GetChannelAsync(cancellationToken);

        try
        {
            return await ConnectCoreAsync(playerChannel, player, server, firstConnection: player.Server is null, anonymous, cancellationToken);
        }
        catch (StreamClosedException) when (playerChannel.IsAlive)
        {
            // A server can close the connection of a player that joins it for a reason that passes, such as a race of its own, so it gets one
            // more try before the player goes to another server
            logger.LogInformation("Server {Server} closed the connection of {Player} player while it joined, so it joins once more", server, player);
            return await ConnectCoreAsync(playerChannel, player, server, firstConnection: player.Server is null, anonymous, cancellationToken);
        }
    }

    private async ValueTask<ConnectionResult> ConnectCoreAsync(INetworkChannel playerChannel, IPlayer player, IServer server, bool firstConnection, bool anonymous, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Since cancellationToken might be coming from ILink, it will be canceled after link is destroyed.
        // All further events should have application lifetime tokens in order to know when they are forced to stop.
        // Also stopping token is used for all events triggered by link.
        cancellationToken = hostApplicationLifetime.ApplicationStopping;

        var unwrappedPlayer = player.Unwrap();

        if (TryGetLink(unwrappedPlayer, out var link))
            await link.StopAsync(cancellationToken);

        logger.LogTrace("Connecting {Player} player to a {Server} server", unwrappedPlayer, server);

        INetworkChannel serverChannel;
        var activated = false;

        try
        {
            serverChannel = await unwrappedPlayer.BuildServerChannelAsync(server, cancellationToken);
        }
        catch (SocketException exception)
        {
            logger.LogWarning("Failed to connect {Player} player to a {Server} server: {ExceptionMessage}", unwrappedPlayer, server, exception.Message);
            return ConnectionResult.NotConnected;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Player {Player} cannot connect to a {Server} server", unwrappedPlayer, server);
            return ConnectionResult.NotConnected;
        }

        try
        {
            link = await events.ThrowWithResultAsync(new CreateLinkEvent(unwrappedPlayer, server, playerChannel, serverChannel), cancellationToken)
                   ?? new Link(unwrappedPlayer, server, playerChannel, serverChannel, logger, events, cancellationToken);

            _weakLinks.Add(link);

            events.RegisterListeners(cancellationToken, link);

            if (!anonymous)
            {
                var side = await events.ThrowWithResultAsync(new AuthenticationStartingEvent(link, unwrappedPlayer), cancellationToken);

                if (side is AuthenticationSide.Proxy && !await unwrappedPlayer.IsProtocolSupportedAsync(cancellationToken))
                {
                    logger.LogWarning("Player {Player} protocol is not supported, forcing authentication to Server side", unwrappedPlayer);
                    side = AuthenticationSide.Server;
                }

                var result = await events.ThrowWithResultAsync(new AuthenticationStartedEvent(link, unwrappedPlayer, side), cancellationToken)
                             ?? AuthenticationResult.NoResult;

                await events.ThrowAsync(new AuthenticationFinishedEvent(link, unwrappedPlayer, side, result), cancellationToken);

                if (!result.IsAuthenticated)
                {
                    await unwrappedPlayer.KickAsync($"You are not authorized to play:\n{result.Message}", cancellationToken);
                    return ConnectionResult.NotConnected;
                }
            }

            using (await _lock.LockAsync(cancellationToken))
                _activeLinks.Add(link);

            activated = true;

            await events.ThrowAsync(new LinkStartedEvent(link, unwrappedPlayer, firstConnection, anonymous), cancellationToken);
            await link.StartAsync(cancellationToken);

            return ConnectionResult.Connected;
        }
        catch (StreamException) when (!playerChannel.IsAlive)
        {
            // The player left while joining. No link is left to stop and end its session, so the kick does, whoever asked for the move.
            await unwrappedPlayer.KickAsync("You left while joining the server", cancellationToken);
            throw;
        }
        catch (StreamException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An error occurred during authentication of {Player} to {Server}", unwrappedPlayer, server);
            await unwrappedPlayer.KickAsync("An internal error occurred during authentication", cancellationToken);

            return ConnectionResult.NotConnected;
        }
        finally
        {
            // A join that never became a link leaves its server connection behind otherwise.
            if (!activated)
                serverChannel.Close();

            if (link is not null)
            {
                if (!_activeLinks.Snapshot.Contains(link))
                {
                    _weakLinks.RemoveAll(weakLink => weakLink == link);
                }
            }
        }
    }
}
