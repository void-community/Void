using Microsoft.Extensions.Logging;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Channels.Extensions;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.Transformations.Extensions;
using Void.Minecraft.Network.Registries.Transformations.Mappings;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_11_1_to_v1_12;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_12_1_to_v1_12_2;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_15_2_to_v1_16;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_20_2_to_v1_20_3;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_21_4_to_v1_21_5;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_7_6_to_v1_8;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations.v1_8_to_v1_9;

namespace Void.Proxy.Plugins.Common.Network.Packets.Transformations;

public static class NetworkTransformations
{
    public static PacketTransformation[] KeepAlive { get; } =
    [
        new KeepAliveTransformation1_8(),
        new KeepAliveTransformation1_12_2()
    ];

    public static PacketTransformation[] ChatMessage { get; } =
    [
        new ChatMessageTransformation1_8(),
        new ChatMessageTransformation1_9(),
        new ChatMessageTransformation1_12(),
        new ChatMessageTransformation1_16()
    ];

    public static PacketTransformation[] SystemChatMessage { get; } =
    [
        new SystemChatMessageTransformation1_20_3(),
        new SystemChatMessageTransformation1_21_5()
    ];

    public static PacketTransformation[] PlayDisconnect { get; } =
    [
        new PlayDisconnectTransformation1_20_3()
    ];

    public static void Register(ILink? link, IPlayer player, Phase phase)
    {
        link ??= player.Link;

        switch (phase)
        {
            case Phase.Play:
                ArgumentNullException.ThrowIfNull(link);
                RegisterMappings<KeepAliveRequestPacket>(link, KeepAlive.SelectMany(keepAlive => keepAlive.Mappings));
                RegisterMappings<KeepAliveResponsePacket>(link, KeepAlive.SelectMany(keepAlive => keepAlive.Mappings));
                RegisterMappings<ChatMessagePacket>(link, ChatMessage.SelectMany(chatMessage => chatMessage.Mappings));
                RegisterMappings<SystemChatMessagePacket>(link, SystemChatMessage.SelectMany(systemChatMessage => systemChatMessage.Mappings));
                RegisterMappings<NbtDisconnectPacket>(link, PlayDisconnect.SelectMany(playDisconnect => playDisconnect.Mappings));
                break;
            case Phase.Configuration:
                // Players are disconnected while they configure too, with the reason as JSON before 1.20.3
                ArgumentNullException.ThrowIfNull(link);
                RegisterMappings<NbtDisconnectPacket>(link, PlayDisconnect.SelectMany(playDisconnect => playDisconnect.Mappings));
                break;
            case Phase.Handshake:
            case Phase.Status:
            case Phase.Login:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    private static void RegisterMappings<T>(ILink link, params IEnumerable<MinecraftPacketTransformationMapping> mappings) where T : IMinecraftPacket
    {
        var logger = link.Player.Logger;
        var protocolVersion = link.Player.ProtocolVersion;

        logger.LogTrace("Registering {PacketType} packet transformations", typeof(T));

        RegisterMappings<T>(link.PlayerChannel, protocolVersion, mappings);
        RegisterMappings<T>(link.ServerChannel, protocolVersion, mappings);
    }

    internal static void RegisterMappings<T>(INetworkChannel channel, ProtocolVersion protocolVersion, params IEnumerable<MinecraftPacketTransformationMapping> mappings) where T : IMinecraftPacket
    {
        var registry = channel.MinecraftRegistries.PacketTransformationsSystem.All;

        if (registry.TryGetFor(typeof(T), TransformationType.Upgrade, out _) || registry.TryGetFor(typeof(T), TransformationType.Downgrade, out _))
            return;

        registry.RegisterTransformations<T>(protocolVersion, mappings);
    }
}
