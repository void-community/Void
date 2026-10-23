using Void.Minecraft.Buffers;
using Void.Minecraft.Nbt;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.World;
using Void.Proxy.Api.Extensions;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Packets.Clientbound;

public class RespawnPacket : IMinecraftClientboundPacket<RespawnPacket>
{
    public required int Dimension { get; set; }
    public required long PartialHashedSeed { get; set; }
    public required short Difficulty { get; set; }
    public required short Gamemode { get; set; }
    public required string? LevelType { get; set; }
    public required byte DataToKeep { get; set; } // 1.16+
    public required DimensionInfo? DimensionInfo { get; set; } // 1.16-1.16.1
    public required short PreviousGamemode { get; set; } // 1.16+
    public required NbtTag? CurrentDimensionData { get; set; } // 1.16.2+
    public required KeyValuePair<string, long> LastDeathPosition { get; set; } // 1.19+
    public required int PortalCooldown { get; set; } // 1.20+

    public static RespawnPacket FromJoinGame(JoinGamePacket packet)
    {
        return FromJoinGame(packet, packet.Dimension);
    }

    public static RespawnPacket FromJoinGame(JoinGamePacket packet, int dimension)
    {
        return new RespawnPacket
        {
            Dimension = dimension,
            PartialHashedSeed = packet.PartialHashedSeed,
            Difficulty = packet.Difficulty,
            Gamemode = packet.Gamemode,
            LevelType = packet.LevelType,
            DataToKeep = 0,
            DimensionInfo = packet.DimensionInfo,
            PreviousGamemode = packet.PreviousGamemode,
            CurrentDimensionData = packet.CurrentDimensionData,
            LastDeathPosition = packet.LastDeathPosition,
            PortalCooldown = packet.PortalCooldown
        };
    }

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
        {
            if (DimensionInfo is null)
                throw new InvalidOperationException($"{nameof(DimensionInfo)} was not set.");

            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2 && protocolVersion < ProtocolVersion.MINECRAFT_1_19)
            {
                if (CurrentDimensionData is not null)
                    buffer.Write(CurrentDimensionData.AsStream());

                buffer.WriteString(DimensionInfo.RegistryIdentifier);
            }
            else
            {
                buffer.WriteString(DimensionInfo.RegistryIdentifier);
                buffer.WriteString(DimensionInfo.LevelName ?? string.Empty);
            }
        }
        else
        {
            buffer.WriteInt(Dimension);
        }

        if (protocolVersion <= ProtocolVersion.MINECRAFT_1_13_2)
            buffer.WriteUnsignedByte((byte)Difficulty);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            buffer.WriteLong(PartialHashedSeed);

        buffer.WriteUnsignedByte((byte)Gamemode);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
        {
            if (DimensionInfo is null)
                throw new InvalidOperationException($"{nameof(DimensionInfo)} was not set.");

            buffer.WriteUnsignedByte((byte)PreviousGamemode);
            buffer.WriteBoolean(DimensionInfo.IsDebugType);
            buffer.WriteBoolean(DimensionInfo.IsFlat);

            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19_3)
                buffer.WriteUnsignedByte(DataToKeep);
            else
                buffer.WriteBoolean(DataToKeep != 0);
        }
        else
        {
            buffer.WriteString(LevelType ?? string.Empty);
        }

        // optional death location
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
        {
            if (LastDeathPosition.IsDefault())
            {
                buffer.WriteBoolean(false);
            }
            else
            {
                buffer.WriteBoolean(true);
                buffer.WriteString(LastDeathPosition.Key);
                buffer.WriteLong(LastDeathPosition.Value);
            }
        }

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20)
            buffer.WriteVarInt(PortalCooldown);
    }

    public static RespawnPacket Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var dimensionIdentifier = string.Empty;
        var levelName = string.Empty;

        var currentDimensionData = (NbtTag?)null;
        var dimension = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
        {
            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2 && protocolVersion < ProtocolVersion.MINECRAFT_1_19)
            {
                var bufferPosition = buffer.Position;
                var data = buffer.ReadToEnd().ToArray(); // TODO remove the allocation
                buffer.Seek(bufferPosition);

                NbtTag.Parse(data, out currentDimensionData);
                dimensionIdentifier = buffer.ReadString();
            }
            else
            {
                dimensionIdentifier = buffer.ReadString();
                levelName = buffer.ReadString();
            }
        }
        else
        {
            dimension = buffer.ReadInt();
        }

        var difficulty = default(byte);
        if (protocolVersion <= ProtocolVersion.MINECRAFT_1_13_2)
            difficulty = buffer.ReadUnsignedByte();

        var partialHashedSeed = default(long);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            partialHashedSeed = buffer.ReadLong();

        var gamemode = buffer.ReadUnsignedByte();

        var previousGamemode = buffer.ReadUnsignedByte();
        var dimensionInfo = default(DimensionInfo);
        var dataToKeep = default(byte);
        var levelType = default(string);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
        {
            previousGamemode = buffer.ReadUnsignedByte();

            var isDebug = buffer.ReadBoolean();
            var isFlat = buffer.ReadBoolean();

            dimensionInfo = new DimensionInfo(dimensionIdentifier, levelName, isFlat, isDebug);

            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19_3)
                dataToKeep = buffer.ReadUnsignedByte();
            else if (buffer.ReadBoolean())
                dataToKeep = 1;
            else
                dataToKeep = 0;
        }
        else
        {
            levelType = buffer.ReadString(16);
        }

        var lastDeathPosition = default(KeyValuePair<string, long>);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19 && buffer.ReadBoolean())
            lastDeathPosition = new KeyValuePair<string, long>(buffer.ReadString(), buffer.ReadLong());

        var portalCooldown = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20)
            portalCooldown = buffer.ReadVarInt();

        return new RespawnPacket
        {
            CurrentDimensionData = currentDimensionData,
            Dimension = dimension,
            Difficulty = difficulty,
            PartialHashedSeed = partialHashedSeed,
            Gamemode = gamemode,
            PreviousGamemode = previousGamemode,
            DimensionInfo = dimensionInfo,
            DataToKeep = dataToKeep,
            LevelType = levelType,
            LastDeathPosition = lastDeathPosition,
            PortalCooldown = portalCooldown
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
