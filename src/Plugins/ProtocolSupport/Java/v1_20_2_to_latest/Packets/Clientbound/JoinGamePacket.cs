using Void.Minecraft.Buffers;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Packets.Clientbound;

/// <summary>
/// Gives the player the world of the server it plays on. The proxy only notes when it passes, so its content goes on as it came.
/// </summary>
public class JoinGamePacket : IMinecraftClientboundPacket<JoinGamePacket>
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        buffer.Write(Data.Span);
    }

    public static JoinGamePacket Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        return new JoinGamePacket { Data = buffer.ReadToEnd().ToArray() };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
