using Void.Minecraft.Buffers;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Packets.Clientbound;

public class RespawnPacket : IMinecraftClientboundPacket<RespawnPacket>
{
    public required int Dimension { get; set; }
    public required short Difficulty { get; set; }
    public required short Gamemode { get; set; }
    public required string? LevelType { get; set; }

    public static RespawnPacket FromJoinGame(JoinGamePacket packet)
    {
        return FromJoinGame(packet, packet.Dimension);
    }

    public static RespawnPacket FromJoinGame(JoinGamePacket packet, int dimension)
    {
        return new RespawnPacket
        {
            Dimension = dimension,
            Difficulty = packet.Difficulty,
            Gamemode = packet.Gamemode,
            LevelType = packet.LevelType
        };
    }

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        buffer.WriteInt(Dimension);
        buffer.WriteUnsignedByte((byte)Difficulty);
        buffer.WriteUnsignedByte((byte)Gamemode);
        buffer.WriteString(LevelType ?? string.Empty);
    }

    public static RespawnPacket Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var dimension = buffer.ReadInt();
        var difficulty = buffer.ReadUnsignedByte();
        var gamemode = buffer.ReadUnsignedByte();
        var levelType = buffer.ReadString(16);

        return new RespawnPacket
        {
            Dimension = dimension,
            Difficulty = difficulty,
            Gamemode = gamemode,
            LevelType = levelType
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
