using Microsoft.Extensions.DependencyInjection;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network.Messages;
using Void.Minecraft.Network.Messages.Packets;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Plugins.Common.Network.Bundles;

namespace Void.Proxy.Plugins.Common.Extensions;

public static class LinkExtensions
{
    public static async ValueTask<T> ReceivePacketAsync<T>(this ILink link, CancellationToken cancellationToken) where T : class, IMinecraftPacket
    {
        return await link.ReceiveCancellablePacketAsync<T>(cancellationToken) ??
            throw new OperationCanceledException($"Packet {typeof(T).Name} was expected to be not canceled");
    }

    public static async ValueTask<T?> ReceiveCancellablePacketAsync<T>(this ILink link, CancellationToken cancellationToken) where T : class, IMinecraftPacket
    {
        var type = typeof(T);

        if (type == typeof(IMinecraftPacket))
            throw new ArgumentException("You should specify any side-bound packet type");

        var side = type.IsAssignableTo(typeof(IMinecraftClientboundPacket)) ? Side.Server : Side.Client;

        return await link.ReceiveCancellablePacketAsync<T>(side, cancellationToken);
    }

    public static async ValueTask<T?> ReceiveCancellablePacketAsync<T>(this ILink link, Side side, CancellationToken cancellationToken) where T : IMinecraftMessage
    {
        if (side is Side.Proxy)
            throw new InvalidOperationException("How would I read packet from proxy?");

        var channel = side is Side.Client ? link.PlayerChannel : link.ServerChannel;
        var packet = await channel.ReceivePacketAsync<T>(cancellationToken);

        var events = link.Player.Context.Services.GetRequiredService<IEventService>();
        var direction = side is Side.Client ? Direction.Serverbound : Direction.Clientbound;
        var cancelled = await events.ThrowWithResultAsync(new MessageReceivedEvent(side, side, Side.Proxy, direction, packet, link, link.Player), cancellationToken);

        return cancelled ? default : packet;
    }

    public static async ValueTask SendTerminalPacketAsync<T>(this ILink link, IBundleService bundles, CancellationToken cancellationToken) where T : class, IMinecraftPacket, new()
    {
        await link.SendTerminalPacketAsync(new T(), bundles, cancellationToken);
    }

    // The bundles of the protocol that sends the packet, as each protocol keeps its own
    public static async ValueTask SendTerminalPacketAsync<T>(this ILink link, T packet, IBundleService bundles, CancellationToken cancellationToken) where T : class, IMinecraftPacket
    {
        await bundles.WaitBundleCompletionAsync();

        await link.SendPacketAsync(packet is IMinecraftClientboundPacket ? Side.Client : Side.Server, packet, cancellationToken);
    }
}
