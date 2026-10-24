using System;
using Void.Minecraft.Buffers;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Xunit;

namespace Void.UnitTests.Network.Packets;

/// <summary>Games read the text of a system chat message or a disconnect as JSON before Minecraft 1.20.3, and as NBT from it.</summary>
public class TextComponentPacketTests
{
    private const string Text = "Stopping proxy...";

    [Fact]
    public void WritesSystemChatAsJsonBefore1_20_3()
    {
        var reader = Encode(new SystemChatMessagePacket { Message = Text, Overlay = false }, ProtocolVersion.MINECRAFT_1_20_2);

        Assert.Equal(Text, new MinecraftBuffer(reader.AsSpan()).ReadComponent(asNbt: false).AsText);
    }

    [Fact]
    public void WritesSystemChatAsNbtFrom1_20_3()
    {
        var reader = Encode(new SystemChatMessagePacket { Message = Text, Overlay = false }, ProtocolVersion.MINECRAFT_1_20_3);

        Assert.Equal(Text, new MinecraftBuffer(reader.AsSpan()).ReadComponent(asNbt: true).AsText);
    }

    [Fact]
    public void WritesDisconnectAsJsonBefore1_20_3()
    {
        var reader = Encode(new NbtDisconnectPacket { Reason = Text }, ProtocolVersion.MINECRAFT_1_20_2);

        Assert.Equal(Text, new MinecraftBuffer(reader.AsSpan()).ReadComponent(asNbt: false).AsText);
    }

    [Fact]
    public void WritesDisconnectAsNbtFrom1_20_3()
    {
        var reader = Encode(new NbtDisconnectPacket { Reason = Text }, ProtocolVersion.MINECRAFT_1_20_3);

        Assert.Equal(Text, new MinecraftBuffer(reader.AsSpan()).ReadComponent(asNbt: true).AsText);
    }

    private static byte[] Encode(IMinecraftPacket packet, ProtocolVersion protocolVersion)
    {
        var output = new byte[1024];
        var buffer = new MinecraftBuffer(output.AsSpan());
        packet.Encode(ref buffer, protocolVersion);

        return output[..(int)buffer.Position];
    }
}
