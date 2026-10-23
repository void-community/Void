using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Components.Text.Properties;
using Void.Minecraft.Components.Text.Properties.Content;
using Void.Minecraft.Events;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Players;
using Void.Proxy.Plugins.Common.Players.Contexts;

namespace Void.Proxy.Plugins.Common.Services.Lifecycle;

public abstract class AbstractLifecycleService(ILogger logger, IEventService events) : IPluginCommonService
{
    private const string DefaultKickMessage = "You were kicked from proxy";

    private readonly KeepAliveForwarding _keepAliveForwarding = new();

    // Players that a server kicked while they played on it, until they play on another server, are kicked, or leave
    private readonly ConcurrentDictionary<IPlayer, (IServer Server, Component Reason)> _serverKicks = [];

    [Subscribe]
    public void OnPlayerConnecting(PlayerConnectingEvent @event)
    {
        @event.Result ??= new SimplePlayer(@event.Client, instance => new PlayerContext(@event.GetServices) { Player = instance });
        logger.LogTrace("Player connecting: {Player}", @event.Result);
    }

    [Subscribe]
    public async ValueTask OnPhaseChanged(PhaseChangedEvent @event, CancellationToken cancellationToken)
    {
        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (@event.Phase is Phase.Play && @event.Side is Side.Server && @event.Player.ProtocolVersion >= ProtocolVersion.MINECRAFT_1_20_2)
        {
            var link = @event.Player.Link ?? throw new InvalidOperationException($"Player has no link assigned in {nameof(Phase.Play)} phase.");
            await events.ThrowAsync(new PlayerJoinedServerEvent(link.Player, link.Server, link, IsRedirected: false), cancellationToken);
        }
    }

    [Subscribe]
    public async ValueTask OnLinkStarted(LinkStartedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (@event.Player.ProtocolVersion >= ProtocolVersion.MINECRAFT_1_20_2)
            return;

        await events.ThrowAsync(new PlayerJoinedServerEvent(@event.Link.Player, @event.Link.Server, @event.Link, IsRedirected: !@event.IsFirstLink), cancellationToken);
    }

    [Subscribe]
    public async ValueTask OnMessageReceived(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        // Plugins such as SlowJoin may answer keep-alives on the player's behalf.
        if (@event.Result)
            return;

        switch (@event.Message)
        {
            case KeepAliveRequestPacket keepAliveRequest:
                await HandleKeepAliveRequestAsync(@event, keepAliveRequest, cancellationToken);
                break;
            case KeepAliveResponsePacket keepAliveResponse:
                HandleKeepAliveResponse(@event, keepAliveResponse);
                break;
            case DisconnectPacket disconnect:
                HandleServerKick(@event, disconnect);
                break;
        }
    }

    [Subscribe]
    public async ValueTask OnPlayerJoinedServer(PlayerJoinedServerEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        if (!_serverKicks.TryRemove(@event.Player, out var kick))
            return;

        var message = new Component(new TextContent($"You were kicked from {kick.Server.Name}: "), new Children([kick.Reason]), Formatting.Default with { Color = "red" }, Interactivity.Default);
        await @event.Player.SendChatMessageAsync(message, cancellationToken);
    }

    [Subscribe]
    public void OnPlayerDisconnected(PlayerDisconnectedEvent @event)
    {
        _serverKicks.TryRemove(@event.Player, out _);
    }

    [Subscribe]
    public async ValueTask OnChatMessageSend(ChatMessageSendEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Origin is not Side.Proxy)
            return;

        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        await SendChatMessageAsync(@event.Player, @event.Text, cancellationToken);
    }

    [Subscribe(PostOrder.Last)]
    public async ValueTask OnPlayerKickEvent(PlayerKickEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        // A player that its server kicked and no other server took is told why the server kicked it
        var reason = _serverKicks.TryRemove(@event.Player, out var serverKick)
            ? serverKick.Reason
            : @event is MinecraftPlayerKickEvent minecraftPlayerKick
                ? minecraftPlayerKick.Reason ?? DefaultKickMessage
                : @event.Text ?? DefaultKickMessage;

        try
        {
            @event.Result = await KickPlayerAsync(@event.Player, reason, cancellationToken);
        }
        catch (StreamException)
        {
            @event.Result = true;
        }
    }

    [Subscribe]
    public async ValueTask OnPlayerVerifiedEncryption(PlayerVerifiedEncryptionEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        await EnableCompressionAsync(@event.Link, cancellationToken);
    }

    protected abstract ValueTask EnableCompressionAsync(ILink link, CancellationToken cancellationToken);
    protected abstract ValueTask<bool> KickPlayerAsync(IPlayer player, Component reason, CancellationToken cancellationToken);
    protected abstract ValueTask<bool> SendChatMessageAsync(IPlayer player, Component text, CancellationToken cancellationToken);
    protected abstract bool IsSupportedVersion(ProtocolVersion version);

    /// <summary>
    /// Forwards the backend's keep-alive to the player, so that the backend decides when the player has timed out, as with a
    /// direct connection. A link that is still joining has no player waiting behind it yet, so the proxy answers for the player.
    /// </summary>
    private async ValueTask HandleKeepAliveRequestAsync(MessageReceivedEvent @event, KeepAliveRequestPacket keepAliveRequest, CancellationToken cancellationToken)
    {
        bool linkForwards = ReferenceEquals(@event.Player.Link, @event.Link) && @event.Link.IsAlive;

        if (!linkForwards)
        {
            @event.Cancel();
            await @event.Link.SendPacketAsync(new KeepAliveResponsePacket { Id = keepAliveRequest.Id }, cancellationToken);
            return;
        }

        _keepAliveForwarding.Remember(@event.Link, keepAliveRequest.Id);
        @event.Player.Logger.LogTrace("Forwarding Keep Alive request {Id} from {Server}", keepAliveRequest.Id, @event.Link.Server);
    }

    /// <summary>
    /// Keeps a kick by the server the player plays on from the player, so that the player moves to another server as when that server
    /// stops, and reads there why it was kicked. A server that the player is still joining refuses it during login instead.
    /// </summary>
    private void HandleServerKick(MessageReceivedEvent @event, DisconnectPacket disconnect)
    {
        var playing = @event.Player.Phase is Phase.Play && ReferenceEquals(@event.Player.Link, @event.Link) && @event.Link.IsAlive;

        if (!playing)
            return;

        @event.Cancel();
        _serverKicks[@event.Player] = (@event.Link.Server, disconnect.Reason);
        logger.LogInformation("Player {Player} was kicked from {Server}: {Reason}", @event.Player, @event.Link.Server, disconnect.Reason.AsText);
    }

    private void HandleKeepAliveResponse(MessageReceivedEvent @event, KeepAliveResponsePacket keepAliveResponse)
    {
        // Clients older than 1.12.2 answer 0 while they load terrain, which backends accept as a sign of life.
        if (KeepAliveTracker.UsesLegacyRequestIdWidth(@event.Player.ProtocolVersion) && keepAliveResponse.Id is 0)
            return;

        if (_keepAliveForwarding.TryAccept(@event.Link, keepAliveResponse.Id))
        {
            @event.Player.Logger.LogTrace("Forwarding Keep Alive response {Id} to {Server}", keepAliveResponse.Id, @event.Link.Server);
            return;
        }

        // Answers to a backend the player has left would otherwise reach the next backend as unexpected ids.
        @event.Cancel();
        @event.Player.Logger.LogDebug("Dropping Keep Alive response {Id} that {Server} did not request", keepAliveResponse.Id, @event.Link.Server);
    }
}
