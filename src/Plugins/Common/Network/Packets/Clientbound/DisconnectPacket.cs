using Void.Minecraft.Buffers;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;

namespace Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

public interface IDisconnectPacketKind<TDisconnectPacket>
{
    public static abstract bool IsNbt { get; }
    public static abstract TDisconnectPacket Create(Component reason);
}

public abstract class DisconnectPacket
{
    public required Component Reason { get; set; }
}

public class DisconnectPacket<TDisconnectPacketKind> : DisconnectPacket, IMinecraftClientboundPacket<TDisconnectPacketKind>
    where TDisconnectPacketKind : DisconnectPacket<TDisconnectPacketKind>, IDisconnectPacketKind<TDisconnectPacketKind>
{
    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        buffer.WriteComponent(Reason, asNbt: IsNbt(protocolVersion));
    }

    public static TDisconnectPacketKind Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        return TDisconnectPacketKind.Create(buffer.ReadComponent(asNbt: IsNbt(protocolVersion)));
    }

    // Disconnects outside of login carry their reason as NBT from Minecraft 1.20.3, and as JSON before, as in 1.20.2.
    private static bool IsNbt(ProtocolVersion protocolVersion)
    {
        return TDisconnectPacketKind.IsNbt && protocolVersion >= ProtocolVersion.MINECRAFT_1_20_3;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

public sealed class JsonDisconnectPacket : DisconnectPacket<JsonDisconnectPacket>, IDisconnectPacketKind<JsonDisconnectPacket>
{
    public static bool IsNbt => false;

    public static JsonDisconnectPacket Create(Component reason) => new() { Reason = reason };
}

public sealed class NbtDisconnectPacket : DisconnectPacket<NbtDisconnectPacket>, IDisconnectPacketKind<NbtDisconnectPacket>
{
    public static bool IsNbt => true;

    public static NbtDisconnectPacket Create(Component reason) => new() { Reason = reason };
}
