using System.Diagnostics.CodeAnalysis;
using Void.Minecraft.Buffers;
using Void.Minecraft.Network;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Network;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Network.Streams.Packet;
using Void.Proxy.Plugins.Common.Services.Channels;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Packets.Clientbound;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Packets.Serverbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Channels;

public class ChannelService : AbstractChannelService
{
    [Subscribe(PostOrder.Last)]
    public static void OnMessageSent(MessageSentEvent @event)
    {
        if (@event.Origin is Side.Proxy)
            return;

        switch (@event.Message)
        {
            case LoginStartPacket:
                @event.Link.PlayerChannel.Pause();
                break;
            case EncryptionRequestPacket:
                @event.Link.PlayerChannel.Resume();
                @event.Link.ServerChannel.Pause();
                break;
            case EncryptionResponsePacket:
                @event.Link.PlayerChannel.Pause();
                @event.Link.ServerChannel.Resume();
                break;
            case SetCompressionPacket:
                @event.Link.PlayerChannel.TryResume();
                break;
            case LoginSuccessPacket:
                @event.Link.PlayerChannel.TryResume();
                break;
        }
    }

    [Subscribe(PostOrder.Last)]
    public static void OnMessageSentLast(MessageSentEvent @event)
    {
        if (@event.Origin is Side.Proxy)
            return;

        if (@event.Message is not EncryptionResponsePacket)
            return;

        // if encryption forced channel to remove packet stream, resume reading with what is left
        if (!@event.Link.PlayerChannel.TryGet<MinecraftPacketMessageStream>(out _))
            @event.Link.PlayerChannel.Resume();
    }

    protected override bool IsSupportedHandshake(Memory<byte> memory, [MaybeNullWhen(false)] out ProtocolVersion protocolVersion)
    {
        var buffer = new MinecraftBuffer(memory.Span);
        var length = buffer.ReadVarInt();
        var packet = buffer.Read(length);

        buffer = new MinecraftBuffer(packet);
        var packetId = buffer.ReadVarInt();

        var decoded = HandshakePacket.Decode(ref buffer, Plugin.SupportedVersions.First());
        protocolVersion = ProtocolVersion.Get(decoded.ProtocolVersion);

        return packetId == 0 && Plugin.SupportedVersions.Contains(protocolVersion) && !buffer.HasData;
    }
}
