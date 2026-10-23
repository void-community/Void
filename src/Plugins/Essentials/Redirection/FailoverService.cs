using Void.Minecraft.Components.Text;
using Void.Minecraft.Components.Text.Properties;
using Void.Minecraft.Components.Text.Properties.Content;
using Void.Minecraft.Events;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

namespace Void.Proxy.Plugins.Essentials.Redirection;

/// <summary>
/// Cancels a kick from the server a player plays on when there is another server, so the proxy moves the player there as when a server stops,
/// and tells the player there why it was kicked. Without another server, or when no other server takes the player, the player is kicked with
/// the server's reason.
/// </summary>
public class FailoverService(IServerService servers) : IEventListener
{
    private (IServer Server, Component Reason)? _kick;

    [Subscribe]
    public void OnMessageReceived(MessageReceivedEvent @event)
    {
        if (@event.Message is not DisconnectPacket disconnect || @event.Result || !@event.Link.IsAlive || @event.Player.Phase is not Phase.Play)
            return;

        if (!servers.All.Any(server => server != @event.Link.Server))
            return;

        @event.Cancel();
        _kick = (@event.Link.Server, disconnect.Reason);
    }

    [Subscribe]
    public async ValueTask OnPlayerJoinedServer(PlayerJoinedServerEvent @event, CancellationToken cancellationToken)
    {
        if (_kick is not var (server, reason))
            return;

        _kick = null;
        await @event.Player.SendChatMessageAsync(new Component(new TextContent($"You were kicked from {server.Name}: "), new Children([reason]), Formatting.Default with { Color = "red" }, Interactivity.Default), cancellationToken);
    }

    [Subscribe]
    public void OnPlayerKick(PlayerKickEvent @event)
    {
        if (_kick is not var (_, reason))
            return;

        _kick = null;
        @event.Text = reason.SerializeLegacy();

        if (@event is MinecraftPlayerKickEvent minecraftPlayerKick)
            minecraftPlayerKick.Reason = reason;
    }
}
