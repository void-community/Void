using Void.Minecraft.Buffers;
using Void.Minecraft.Nbt;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.World;
using Void.Proxy.Api.Extensions;

namespace Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

public class JoinGamePacket : IMinecraftClientboundPacket<JoinGamePacket>
{
    private const int HardcoreFlag = 0x08;

    // The previous game mode a player without one has, as servers send it before 26.3
    private const short NoPreviousGamemode = 0xFF;

    public required int EntityId { get; set; }
    public required short Gamemode { get; set; }
    public required int Dimension { get; set; } // before 1.16
    public required long PartialHashedSeed { get; set; } // 1.15+
    public required short Difficulty { get; set; } // before 1.14
    public required bool IsHardcore { get; set; }
    public required int MaxPlayers { get; set; }
    public required string? LevelType { get; set; } // before 1.16
    public required int ViewDistance { get; set; } // 1.14+
    public required bool ReducedDebugInfo { get; set; } // 1.8+
    public required bool ShowRespawnScreen { get; set; } // 1.15+
    public required bool DoLimitedCrafting { get; set; } // 1.20.2+
    public required string[] LevelNames { get; set; } // 1.16+
    public required NbtTag? Registry { get; set; } // 1.16 to 1.20.1
    public required DimensionInfo? DimensionInfo { get; set; } // 1.16+, without a dimension type name from 1.20.5
    public required int DimensionTypeId { get; set; } // 1.20.5+
    public required NbtTag? CurrentDimensionData { get; set; } // 1.16.2 to 1.18.2
    public required short PreviousGamemode { get; set; } // 1.16+
    public required int SimulationDistance { get; set; } // 1.18+
    public required KeyValuePair<string, long> LastDeathPosition { get; set; } // 1.19+
    public required int PortalCooldown { get; set; } // 1.20+
    public required int SeaLevel { get; set; } // 1.21.2+
    public required bool OnlineMode { get; set; } // 26.2+
    public required bool EnforcesSecureChat { get; set; } // 1.20.5+

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_2)
            Encode1202Up(ref buffer, protocolVersion);
        else if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
            Encode116Up(ref buffer, protocolVersion);
        else
            EncodeLegacy(ref buffer, protocolVersion);
    }

    public static JoinGamePacket Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_2)
            return Decode1202Up(ref buffer, protocolVersion);
        else if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16)
            return Decode116Up(ref buffer, protocolVersion);
        else
            return DecodeLegacy(ref buffer, protocolVersion);
    }

    private static JoinGamePacket DecodeLegacy(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var entityId = buffer.ReadInt();
        var gamemode = (short)buffer.ReadUnsignedByte();
        var isHardcore = (gamemode & HardcoreFlag) != 0;
        gamemode &= ~HardcoreFlag;

        var dimension = protocolVersion < ProtocolVersion.MINECRAFT_1_9_1 ?
            unchecked((sbyte)buffer.ReadUnsignedByte()) :
            buffer.ReadInt();

        var difficulty = default(short);
        if (protocolVersion <= ProtocolVersion.MINECRAFT_1_13_2)
            difficulty = buffer.ReadUnsignedByte();

        var partialHashedSeed = default(long);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            partialHashedSeed = buffer.ReadLong();

        var maxPlayers = buffer.ReadUnsignedByte();
        var levelType = buffer.ReadString(16);

        var viewDistance = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_14)
            viewDistance = buffer.ReadVarInt();

        var reducedDebugInfo = default(bool);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_8)
            reducedDebugInfo = buffer.ReadBoolean();

        var showRespawnScreen = default(bool);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            showRespawnScreen = buffer.ReadBoolean();

        return new JoinGamePacket
        {
            EntityId = entityId,
            Gamemode = gamemode,
            IsHardcore = isHardcore,
            Dimension = dimension,
            Difficulty = difficulty,
            PartialHashedSeed = partialHashedSeed,
            MaxPlayers = maxPlayers,
            LevelType = levelType,
            ViewDistance = viewDistance,
            ReducedDebugInfo = reducedDebugInfo,
            ShowRespawnScreen = showRespawnScreen,

            // fields not present before 1.16
            DoLimitedCrafting = false,
            LevelNames = [],
            Registry = null,
            DimensionInfo = null,
            DimensionTypeId = 0,
            CurrentDimensionData = null,
            PreviousGamemode = 0,
            SimulationDistance = 0,
            LastDeathPosition = default,
            PortalCooldown = 0,
            SeaLevel = 0,
            OnlineMode = false,
            EnforcesSecureChat = false
        };
    }

    private static JoinGamePacket Decode116Up(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var entityId = buffer.ReadInt();

        short gamemode;
        bool isHardcore;
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2)
        {
            isHardcore = buffer.ReadBoolean();
            gamemode = buffer.ReadUnsignedByte();
        }
        else
        {
            gamemode = buffer.ReadUnsignedByte();
            isHardcore = (gamemode & HardcoreFlag) != 0;
            gamemode &= ~HardcoreFlag;
        }

        var previousGamemode = buffer.ReadUnsignedByte();

        var levelNames = new string[buffer.ReadVarInt()];
        for (var i = 0; i < levelNames.Length; i++)
            levelNames[i] = buffer.ReadString();

        var bufferPosition = buffer.Position;
        var registryLength = NbtTag.Parse(buffer.ReadToEnd().ToArray(), out var registry, true);
        buffer.Seek(bufferPosition + registryLength);
        var levelName = string.Empty;

        string? dimensionIdentifier;
        NbtTag? currentDimensionData = null;
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2 && protocolVersion < ProtocolVersion.MINECRAFT_1_19)
        {
            bufferPosition = buffer.Position;
            var length = NbtTag.Parse(buffer.ReadToEnd().ToArray(), out currentDimensionData, true);
            buffer.Seek(bufferPosition + length);

            dimensionIdentifier = buffer.ReadString();
        }
        else
        {
            dimensionIdentifier = buffer.ReadString();
            levelName = buffer.ReadString();
        }

        var partialHashedSeed = buffer.ReadLong();

        int maxPlayers;
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2)
            maxPlayers = buffer.ReadVarInt();
        else
            maxPlayers = buffer.ReadUnsignedByte();

        var viewDistance = buffer.ReadVarInt();

        var simulationDistance = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_18)
            simulationDistance = buffer.ReadVarInt();

        var reducedDebugInfo = buffer.ReadBoolean();
        var showRespawnScreen = buffer.ReadBoolean();

        var isDebug = buffer.ReadBoolean();
        var isFlat = buffer.ReadBoolean();
        var dimensionInfo = new DimensionInfo(dimensionIdentifier, levelName, isFlat, isDebug);

        // optional death location
        var lastDeathPosition = default(KeyValuePair<string, long>);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19 && buffer.ReadBoolean())
            lastDeathPosition = new KeyValuePair<string, long>(buffer.ReadString(), buffer.ReadLong());

        var portalCooldown = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20)
            portalCooldown = buffer.ReadVarInt();

        return new JoinGamePacket
        {
            EntityId = entityId,
            IsHardcore = isHardcore,
            Gamemode = gamemode,
            PreviousGamemode = previousGamemode,
            LevelNames = levelNames,
            Registry = registry,
            PartialHashedSeed = partialHashedSeed,
            MaxPlayers = maxPlayers,
            ViewDistance = viewDistance,
            SimulationDistance = simulationDistance,
            ReducedDebugInfo = reducedDebugInfo,
            ShowRespawnScreen = showRespawnScreen,
            DimensionInfo = dimensionInfo,
            LastDeathPosition = lastDeathPosition,
            PortalCooldown = portalCooldown,
            CurrentDimensionData = currentDimensionData,

            // fields not present from 1.16 to 1.20.1
            Dimension = 0,
            LevelType = null,
            Difficulty = 0,
            DoLimitedCrafting = false,
            DimensionTypeId = 0,
            SeaLevel = 0,
            OnlineMode = false,
            EnforcesSecureChat = false
        };
    }

    private static JoinGamePacket Decode1202Up(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var entityId = buffer.ReadInt();
        var isHardcore = buffer.ReadBoolean();

        var levelNames = new string[buffer.ReadVarInt()];
        for (var i = 0; i < levelNames.Length; i++)
            levelNames[i] = buffer.ReadString();

        var maxPlayers = buffer.ReadVarInt();
        var viewDistance = buffer.ReadVarInt();
        var simulationDistance = buffer.ReadVarInt();
        var reducedDebugInfo = buffer.ReadBoolean();
        var showRespawnScreen = buffer.ReadBoolean();
        var doLimitedCrafting = buffer.ReadBoolean();

        // the dimension type is sent by name before 1.20.5, and by its registry id since
        var dimensionIdentifier = string.Empty;
        var dimensionTypeId = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5)
            dimensionTypeId = buffer.ReadVarInt();
        else
            dimensionIdentifier = buffer.ReadString();

        var levelName = buffer.ReadString();
        var partialHashedSeed = buffer.ReadLong();

        short gamemode;
        short previousGamemode;
        if (protocolVersion >= ProtocolVersion.MINECRAFT_26_3)
        {
            gamemode = (short)buffer.ReadVarInt();

            // 0 when there is no previous game mode, otherwise the game mode plus 1
            var previousGamemodeId = buffer.ReadVarInt();
            previousGamemode = previousGamemodeId is 0 ? NoPreviousGamemode : (short)(previousGamemodeId - 1);
        }
        else
        {
            gamemode = buffer.ReadUnsignedByte();
            previousGamemode = buffer.ReadUnsignedByte();
        }

        var isDebug = buffer.ReadBoolean();
        var isFlat = buffer.ReadBoolean();
        var dimensionInfo = new DimensionInfo(dimensionIdentifier, levelName, isFlat, isDebug);

        // optional death location
        var lastDeathPosition = default(KeyValuePair<string, long>);
        if (buffer.ReadBoolean())
            lastDeathPosition = new KeyValuePair<string, long>(buffer.ReadString(), buffer.ReadLong());

        var portalCooldown = buffer.ReadVarInt();

        var seaLevel = default(int);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_21_2)
            seaLevel = buffer.ReadVarInt();

        var onlineMode = default(bool);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_26_2)
            onlineMode = buffer.ReadBoolean();

        var enforcesSecureChat = default(bool);
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5)
            enforcesSecureChat = buffer.ReadBoolean();

        return new JoinGamePacket
        {
            EntityId = entityId,
            IsHardcore = isHardcore,
            LevelNames = levelNames,
            MaxPlayers = maxPlayers,
            ViewDistance = viewDistance,
            SimulationDistance = simulationDistance,
            ReducedDebugInfo = reducedDebugInfo,
            ShowRespawnScreen = showRespawnScreen,
            DoLimitedCrafting = doLimitedCrafting,
            DimensionTypeId = dimensionTypeId,
            DimensionInfo = dimensionInfo,
            PartialHashedSeed = partialHashedSeed,
            Gamemode = gamemode,
            PreviousGamemode = previousGamemode,
            LastDeathPosition = lastDeathPosition,
            PortalCooldown = portalCooldown,
            SeaLevel = seaLevel,
            OnlineMode = onlineMode,
            EnforcesSecureChat = enforcesSecureChat,

            // fields not present from 1.20.2
            Dimension = 0,
            LevelType = null,
            Difficulty = 0,
            Registry = null,
            CurrentDimensionData = null
        };
    }

    private void EncodeLegacy(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        buffer.WriteInt(EntityId);
        buffer.WriteUnsignedByte((byte)(IsHardcore ? Gamemode | HardcoreFlag : Gamemode));

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_9_1)
            buffer.WriteInt(Dimension);
        else
            buffer.WriteUnsignedByte((byte)Dimension);

        if (protocolVersion <= ProtocolVersion.MINECRAFT_1_13_2)
            buffer.WriteUnsignedByte((byte)Difficulty);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            buffer.WriteLong(PartialHashedSeed);

        buffer.WriteUnsignedByte((byte)MaxPlayers);

        if (LevelType is null)
            throw new InvalidOperationException($"{nameof(LevelType)} was not set.");

        buffer.WriteString(LevelType);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_14)
            buffer.WriteVarInt(ViewDistance);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_8)
            buffer.WriteBoolean(ReducedDebugInfo);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_15)
            buffer.WriteBoolean(ShowRespawnScreen);
    }

    private void Encode116Up(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (DimensionInfo is null)
            throw new InvalidOperationException($"{nameof(DimensionInfo)} was not set.");

        buffer.WriteInt(EntityId);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2)
        {
            buffer.WriteBoolean(IsHardcore);
            buffer.WriteUnsignedByte((byte)Gamemode);
        }
        else
        {
            buffer.WriteUnsignedByte((byte)(IsHardcore ? Gamemode | HardcoreFlag : Gamemode));
        }

        buffer.WriteUnsignedByte((byte)PreviousGamemode);

        buffer.WriteVarInt(LevelNames.Length);
        foreach (var levelName in LevelNames)
            buffer.WriteString(levelName);

        if (Registry is not null)
            buffer.Write(Registry.AsStream());

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

        buffer.WriteLong(PartialHashedSeed);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2)
            buffer.WriteVarInt(MaxPlayers);
        else
            buffer.WriteUnsignedByte((byte)MaxPlayers);

        buffer.WriteVarInt(ViewDistance);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_18)
            buffer.WriteVarInt(SimulationDistance);

        buffer.WriteBoolean(ReducedDebugInfo);
        buffer.WriteBoolean(ShowRespawnScreen);

        buffer.WriteBoolean(DimensionInfo.IsDebugType);
        buffer.WriteBoolean(DimensionInfo.IsFlat);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
            WriteLastDeathPosition(ref buffer);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20)
            buffer.WriteVarInt(PortalCooldown);
    }

    private void Encode1202Up(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (DimensionInfo is null)
            throw new InvalidOperationException($"{nameof(DimensionInfo)} was not set.");

        buffer.WriteInt(EntityId);
        buffer.WriteBoolean(IsHardcore);

        buffer.WriteVarInt(LevelNames.Length);
        foreach (var levelName in LevelNames)
            buffer.WriteString(levelName);

        buffer.WriteVarInt(MaxPlayers);
        buffer.WriteVarInt(ViewDistance);
        buffer.WriteVarInt(SimulationDistance);
        buffer.WriteBoolean(ReducedDebugInfo);
        buffer.WriteBoolean(ShowRespawnScreen);
        buffer.WriteBoolean(DoLimitedCrafting);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5)
            buffer.WriteVarInt(DimensionTypeId);
        else
            buffer.WriteString(DimensionInfo.RegistryIdentifier);

        buffer.WriteString(DimensionInfo.LevelName ?? string.Empty);
        buffer.WriteLong(PartialHashedSeed);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_26_3)
        {
            buffer.WriteVarInt(Gamemode);
            buffer.WriteVarInt(PreviousGamemode is NoPreviousGamemode ? 0 : PreviousGamemode + 1);
        }
        else
        {
            buffer.WriteUnsignedByte((byte)Gamemode);
            buffer.WriteUnsignedByte((byte)PreviousGamemode);
        }

        buffer.WriteBoolean(DimensionInfo.IsDebugType);
        buffer.WriteBoolean(DimensionInfo.IsFlat);

        WriteLastDeathPosition(ref buffer);

        buffer.WriteVarInt(PortalCooldown);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_21_2)
            buffer.WriteVarInt(SeaLevel);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_26_2)
            buffer.WriteBoolean(OnlineMode);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5)
            buffer.WriteBoolean(EnforcesSecureChat);
    }

    private void WriteLastDeathPosition(ref MinecraftBuffer buffer)
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
