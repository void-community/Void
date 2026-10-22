using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Void.Proxy.Plugins.ModsSupport.Forge;
using Xunit;

namespace Void.UnitTests.Plugins;

public class LegacyHandshakeTests
{
    // FML for Minecraft 1.7.2 names protocol 1 in its hellos, and its acknowledgements name the phase they complete.
    private static readonly byte[] ServerHello = [0x00, 0x01];
    private static readonly byte[] ClientHello = [0x01, 0x01];
    private static readonly byte[] ModList = [0x02, 0x00];
    private static readonly byte[] ServerWaitingAcknowledgement = [0xFF, 0x02];
    private static readonly byte[] ServerCompleteAcknowledgement = [0xFF, 0x03];
    private static readonly byte[] Register = Encoding.UTF8.GetBytes("FML|HS\0FML\0FML|MP\0FML\0FORGE");

    [Fact]
    public void AnswersEachServerMessageAsThePlayerAnsweredTheFirstServer()
    {
        var handshake = Record(ModIdData(("minecraft:stone", 1), ("storagedrawers:basicDrawers", 175)));

        Assert.Equal([Answer("REGISTER", Register), Answer(LegacyHandshake.Channel, ClientHello), Answer(LegacyHandshake.Channel, ModList)], Answers(handshake, ServerHello));
        Assert.Equal([Answer(LegacyHandshake.Channel, Acknowledgement(2))], Answers(handshake, ModList));
        Assert.Equal([Answer(LegacyHandshake.Channel, Acknowledgement(3))], Answers(handshake, ModIdData(("minecraft:stone", 1))));
        Assert.Equal([Answer(LegacyHandshake.Channel, Acknowledgement(4))], Answers(handshake, ServerWaitingAcknowledgement));
        Assert.Equal([Answer(LegacyHandshake.Channel, Acknowledgement(5))], Answers(handshake, ServerCompleteAcknowledgement));
    }

    [Fact]
    public void AnswersNothingTheFirstServerNeverAsked()
    {
        var handshake = Record(ModIdData(("minecraft:stone", 1)));

        Assert.Empty(Answers(handshake, [0xFF, 0x04]));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void OnlyPlayersOfFmlProtocol1CantReset(byte protocol, bool canReset)
    {
        var handshake = new LegacyHandshake();
        handshake.RecordServerMessage(ServerHello);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, [0x01, protocol]);

        Assert.Equal(canReset, handshake.CanReset);
    }

    [Fact]
    public void PlayersThatNeverSentAHelloCanReset()
    {
        var handshake = new LegacyHandshake();
        handshake.RecordServerMessage(ServerHello);

        Assert.True(handshake.CanReset);
    }

    [Fact]
    public void KeepsTheSameIdsInAnyOrder()
    {
        var handshake = Record(ModIdData(("minecraft:stone", 1), ("storagedrawers:basicDrawers", 175)));

        Assert.True(handshake.KeepsIds(ModIdData(("storagedrawers:basicDrawers", 175), ("minecraft:stone", 1))));
    }

    [Theory]
    [InlineData("storagedrawers:basicDrawers", 176)]
    [InlineData("storagedrawers:compDrawers", 175)]
    public void DoesNotKeepOtherIds(string name, int id)
    {
        var handshake = Record(ModIdData(("minecraft:stone", 1), ("storagedrawers:basicDrawers", 175)));

        Assert.False(handshake.KeepsIds(ModIdData(("minecraft:stone", 1), (name, id))));
        Assert.False(handshake.KeepsIds(ModIdData(("minecraft:stone", 1))));
    }

    [Fact]
    public void KeepsIdsThroughMessagesWithoutThem()
    {
        var handshake = Record(ModIdData(("minecraft:stone", 1)));

        Assert.True(handshake.KeepsIds(ServerHello));
        Assert.True(new LegacyHandshake().KeepsIds(ModIdData(("minecraft:stone", 1))));
    }

    // The first handshake of a Forge 1.7.2 player, as the proxy sees it.
    private static LegacyHandshake Record(byte[] modIdData)
    {
        var handshake = new LegacyHandshake();

        handshake.RecordServerMessage(ServerHello);
        handshake.RecordPlayerMessage("REGISTER", Register);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, ClientHello);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, ModList);
        handshake.RecordServerMessage(ModList);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, Acknowledgement(2));
        handshake.RecordServerMessage(modIdData);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, Acknowledgement(3));
        handshake.RecordServerMessage(ServerWaitingAcknowledgement);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, Acknowledgement(4));
        handshake.RecordServerMessage(ServerCompleteAcknowledgement);
        handshake.RecordPlayerMessage(LegacyHandshake.Channel, Acknowledgement(5));

        return handshake;
    }

    private static List<string> Answers(LegacyHandshake handshake, byte[] serverMessage)
    {
        return [.. handshake.AnswersTo(serverMessage).Select(answer => Answer(answer.Channel, answer.Data.ToArray()))];
    }

    private static string Answer(string channel, byte[] data)
    {
        return $"{channel} {Convert.ToHexString(data)}";
    }

    private static byte[] Acknowledgement(byte phase)
    {
        return [0xFF, phase];
    }

    private static byte[] ModIdData(params (string Name, int Id)[] ids)
    {
        List<byte> data = [0x03];
        WriteVarInt(data, ids.Length);

        foreach (var (name, id) in ids)
        {
            var encodedName = Encoding.UTF8.GetBytes(name);
            WriteVarInt(data, encodedName.Length);
            data.AddRange(encodedName);
            WriteVarInt(data, id);
        }

        return [.. data];
    }

    private static void WriteVarInt(List<byte> data, int value)
    {
        for (; value >= 0x80; value >>= 7)
            data.Add((byte)(value | 0x80));

        data.Add((byte)value);
    }
}
