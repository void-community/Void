using System.Collections.Generic;
using System.IO;
using System.Linq;
using Void.Minecraft.Buffers;
using Void.Minecraft.Nbt.Tags;
using Void.Minecraft.Network;
using Void.Minecraft.World;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Xunit;

namespace Void.UnitTests.Network.Packets;

public class JoinGamePacketTests
{
    public static IEnumerable<object[]> ProtocolVersions => ProtocolVersion.Range().Select(protocolVersion => new object[] { protocolVersion });

    [Theory]
    [MemberData(nameof(ProtocolVersions))]
    public void ReadsBackWhatItWritesForEveryVersion(ProtocolVersion protocolVersion)
    {
        var written = Encode(CreatePacket(protocolVersion), protocolVersion);

        var buffer = new MinecraftBuffer(new MemoryStream(written));
        var read = JoinGamePacket.Decode(ref buffer, protocolVersion);

        Assert.False(buffer.HasData);
        Assert.Equal(written, Encode(read, protocolVersion));
    }

    [Fact]
    public void ReadsTheLayoutOf1_20_2()
    {
        var written = Write((ref MinecraftBuffer buffer) =>
        {
            buffer.WriteInt(42);
            buffer.WriteBoolean(true);
            buffer.WriteVarInt(1);
            buffer.WriteString("minecraft:overworld");
            buffer.WriteVarInt(20);
            buffer.WriteVarInt(10);
            buffer.WriteVarInt(8);
            buffer.WriteBoolean(false);
            buffer.WriteBoolean(true);
            buffer.WriteBoolean(true);
            buffer.WriteString("minecraft:overworld_caves");
            buffer.WriteString("minecraft:overworld");
            buffer.WriteLong(123);
            buffer.WriteUnsignedByte(1);
            buffer.WriteUnsignedByte(0xFF);
            buffer.WriteBoolean(false);
            buffer.WriteBoolean(true);
            buffer.WriteBoolean(false);
            buffer.WriteVarInt(5);
        });

        var buffer = new MinecraftBuffer(new MemoryStream(written));
        var packet = JoinGamePacket.Decode(ref buffer, ProtocolVersion.MINECRAFT_1_20_2);

        Assert.False(buffer.HasData);
        Assert.Equal(42, packet.EntityId);
        Assert.True(packet.IsHardcore);
        Assert.Equal(["minecraft:overworld"], packet.LevelNames);
        Assert.Equal(20, packet.MaxPlayers);
        Assert.Equal(10, packet.ViewDistance);
        Assert.Equal(8, packet.SimulationDistance);
        Assert.True(packet.ShowRespawnScreen);
        Assert.True(packet.DoLimitedCrafting);
        Assert.Equal(new DimensionInfo("minecraft:overworld_caves", "minecraft:overworld", IsFlat: true, IsDebugType: false), packet.DimensionInfo);
        Assert.Equal(123, packet.PartialHashedSeed);
        Assert.Equal(1, packet.Gamemode);
        Assert.Equal(0xFF, packet.PreviousGamemode);
        Assert.Equal(5, packet.PortalCooldown);
    }

    [Fact]
    public void ReadsTheLayoutOf26_3()
    {
        var written = Write((ref MinecraftBuffer buffer) =>
        {
            buffer.WriteInt(42);
            buffer.WriteBoolean(false);
            buffer.WriteVarInt(1);
            buffer.WriteString("minecraft:overworld");
            buffer.WriteVarInt(20);
            buffer.WriteVarInt(10);
            buffer.WriteVarInt(8);
            buffer.WriteBoolean(false);
            buffer.WriteBoolean(true);
            buffer.WriteBoolean(false);
            buffer.WriteVarInt(3);
            buffer.WriteString("minecraft:overworld");
            buffer.WriteLong(123);
            buffer.WriteVarInt(2);
            buffer.WriteVarInt(0);
            buffer.WriteBoolean(false);
            buffer.WriteBoolean(false);
            buffer.WriteBoolean(true);
            buffer.WriteString("minecraft:the_nether");
            buffer.WriteLong(456);
            buffer.WriteVarInt(5);
            buffer.WriteVarInt(63);
            buffer.WriteBoolean(true);
            buffer.WriteBoolean(true);
        });

        var buffer = new MinecraftBuffer(new MemoryStream(written));
        var packet = JoinGamePacket.Decode(ref buffer, ProtocolVersion.MINECRAFT_26_3);

        Assert.False(buffer.HasData);
        Assert.Equal(3, packet.DimensionTypeId);
        Assert.Equal("minecraft:overworld", packet.DimensionInfo?.LevelName);
        Assert.Equal(2, packet.Gamemode);
        Assert.Equal(0xFF, packet.PreviousGamemode);
        Assert.Equal(new KeyValuePair<string, long>("minecraft:the_nether", 456), packet.LastDeathPosition);
        Assert.Equal(5, packet.PortalCooldown);
        Assert.Equal(63, packet.SeaLevel);
        Assert.True(packet.OnlineMode);
        Assert.True(packet.EnforcesSecureChat);
        Assert.Equal(written, Encode(packet, ProtocolVersion.MINECRAFT_26_3));
    }

    [Theory]
    [InlineData(0xFF, 0xFF, 0x00)]
    [InlineData(1, 0x01, 0x02)]
    public void WritesThePreviousGameModeAsEachVersionSendsIt(short previousGamemode, byte before26_3, byte since26_3)
    {
        var packet = CreatePacket(ProtocolVersion.MINECRAFT_26_2);
        packet.LevelNames = [];
        packet.DimensionInfo = new DimensionInfo(string.Empty, string.Empty, IsFlat: false, IsDebugType: false);
        packet.PreviousGamemode = previousGamemode;

        Assert.Equal(before26_3, PreviousGamemodeByte(packet, ProtocolVersion.MINECRAFT_26_2));
        Assert.Equal(since26_3, PreviousGamemodeByte(packet, ProtocolVersion.MINECRAFT_26_3));

        // entity id, hardcore, no level names, three single byte varints, three booleans, dimension type, empty level name, seed, game mode
        static byte PreviousGamemodeByte(JoinGamePacket packet, ProtocolVersion protocolVersion) => Encode(packet, protocolVersion)[4 + 1 + 1 + 3 + 3 + 1 + 1 + 8 + 1];
    }

    private static JoinGamePacket CreatePacket(ProtocolVersion protocolVersion)
    {
        var hasRegistry = protocolVersion >= ProtocolVersion.MINECRAFT_1_16 && protocolVersion < ProtocolVersion.MINECRAFT_1_20_2;
        var hasDimensionData = protocolVersion >= ProtocolVersion.MINECRAFT_1_16_2 && protocolVersion < ProtocolVersion.MINECRAFT_1_19;
        var hasDimensionTypeName = protocolVersion < ProtocolVersion.MINECRAFT_1_20_5;

        return new JoinGamePacket
        {
            EntityId = 42,
            Gamemode = 1,
            IsHardcore = true,
            Dimension = protocolVersion < ProtocolVersion.MINECRAFT_1_16 ? -1 : 0,
            PartialHashedSeed = protocolVersion >= ProtocolVersion.MINECRAFT_1_15 ? 123 : 0,
            Difficulty = (short)(protocolVersion <= ProtocolVersion.MINECRAFT_1_13_2 ? 2 : 0),
            MaxPlayers = 20,
            LevelType = protocolVersion < ProtocolVersion.MINECRAFT_1_16 ? "flat" : null,
            ViewDistance = protocolVersion >= ProtocolVersion.MINECRAFT_1_14 ? 10 : 0,
            ReducedDebugInfo = protocolVersion >= ProtocolVersion.MINECRAFT_1_8,
            ShowRespawnScreen = protocolVersion >= ProtocolVersion.MINECRAFT_1_15,
            DoLimitedCrafting = protocolVersion >= ProtocolVersion.MINECRAFT_1_20_2,
            LevelNames = protocolVersion >= ProtocolVersion.MINECRAFT_1_16 ? ["minecraft:overworld", "minecraft:the_nether"] : [],
            Registry = hasRegistry ? new NbtCompound { ["minecraft:dimension_type"] = new NbtString("overworld") } : null,
            DimensionInfo = protocolVersion >= ProtocolVersion.MINECRAFT_1_16 ? new DimensionInfo(hasDimensionTypeName ? "minecraft:overworld" : string.Empty, hasDimensionData ? string.Empty : "minecraft:overworld", IsFlat: true, IsDebugType: false) : null,
            DimensionTypeId = protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5 ? 3 : 0,
            CurrentDimensionData = hasDimensionData ? new NbtCompound { ["natural"] = new NbtByte(1) } : null,
            PreviousGamemode = (short)(protocolVersion >= ProtocolVersion.MINECRAFT_1_16 ? 0xFF : 0),
            SimulationDistance = protocolVersion >= ProtocolVersion.MINECRAFT_1_18 ? 8 : 0,
            LastDeathPosition = protocolVersion >= ProtocolVersion.MINECRAFT_1_19 ? new KeyValuePair<string, long>("minecraft:the_nether", 456) : default,
            PortalCooldown = protocolVersion >= ProtocolVersion.MINECRAFT_1_20 ? 5 : 0,
            SeaLevel = protocolVersion >= ProtocolVersion.MINECRAFT_1_21_2 ? 63 : 0,
            OnlineMode = protocolVersion >= ProtocolVersion.MINECRAFT_26_2,
            EnforcesSecureChat = protocolVersion >= ProtocolVersion.MINECRAFT_1_20_5
        };
    }

    private static byte[] Encode(JoinGamePacket packet, ProtocolVersion protocolVersion)
    {
        return Write((ref MinecraftBuffer buffer) => packet.Encode(ref buffer, protocolVersion));
    }

    private static byte[] Write(WriteAction write)
    {
        using var stream = new MemoryStream();
        var buffer = new MinecraftBuffer(stream);
        write(ref buffer);

        return stream.ToArray();
    }

    private delegate void WriteAction(ref MinecraftBuffer buffer);
}
