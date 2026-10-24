using Microsoft.Extensions.Logging;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Events;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Players;
using Void.Proxy.Plugins.Common.Players.Contexts;

namespace Void.Proxy.Plugins.Common.Services.Lifecycle;

public abstract class AbstractLifecycleService(ILogger logger, IEventService events, IPlayerService players) : IPluginCommonService
{
    private const string DefaultKickMessage = "You were kicked from proxy";

    private readonly KeepAliveForwarding _keepAliveForwarding = new();

    [Subscribe]
    public void OnPlayerConnecting(PlayerConnectingEvent @event)
    {
        @event.Result ??= new SimplePlayer(@event.Client, instance => new PlayerContext(@event.GetServices) { Player = instance });
        logger.LogTrace("Player connecting: {Player}", @event.Result);
    }

    // Players of the versions the plugin supports can't play without it, so they leave with why rather than with a game that stops working.
    [Subscribe]
    public async ValueTask OnPluginUnloading(PluginUnloadingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Plugin.GetType().Assembly != GetType().Assembly)
            return;

        await players.ForEachAsync(async (player, cancellationToken) =>
        {
            if (!player.IsMinecraft || !IsSupportedVersion(player.ProtocolVersion))
                return;

            try
            {
                await player.KickAsync($"Void unloaded support for your Minecraft version ({player.ProtocolVersion})", cancellationToken);
            }
            catch (StreamClosedException)
            {
                // Player disconnected before we could kick them, ignore
            }
        }, cancellationToken);
    }

    [Subscribe]
    public async ValueTask OnMessageSent(MessageSentEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        // Players older than 1.20.2 get their Join Game while they join, and start to play when the link starts
        if (@event.Player.ProtocolVersion < ProtocolVersion.MINECRAFT_1_20_2)
            return;

        // The game has no world of the server until its Join Game, so the player starts to play there only once that reaches it
        if (@event.Message is not JoinGamePacket)
            return;

        await events.ThrowAsync(new PlayerJoinedServerEvent(@event.Link.Player, @event.Link.Server, @event.Link, IsRedirected: false), cancellationToken);
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
        }
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

        var reason = @event is MinecraftPlayerKickEvent minecraftPlayerKick
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
