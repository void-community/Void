using System;
using System.IO;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Proxy.Plugins.ModsSupport.Forge;
using Xunit;

namespace Void.UnitTests.Plugins;

public class LoginWrapperMessageTests
{
    // The mod data request a NeoForge 20.2.93 server sent, which repeats the request's index and channel before the wrapped message.
    private const string NeoForgeModData = "0010666D6C3A6C6F67696E777261707065720D666D6C3A68616E647368616B654E0503096D696E656372616674094D696E65637261667406312E32302E3207776164646C657307576164646C657306302E31302E30086E656F666F726765084E656F466F7267650732302E322E3933";

    [Fact]
    public void ReadsMessagesNeoForgeRepeatsTheRequestHeaderBefore()
    {
        Assert.Equal(new LoginWrapperMessage("fml:handshake", 5), LoginWrapperMessage.Read(Convert.FromHexString(NeoForgeModData)));
    }

    [Fact]
    public void ReadsMessagesForgeWrapsAlone()
    {
        // The same message as Forge sends it, without the repeated header.
        var payload = Convert.FromHexString(NeoForgeModData)[18..];

        Assert.Equal(new LoginWrapperMessage("fml:handshake", 5), LoginWrapperMessage.Read(payload));
    }

    [Theory]
    [InlineData("fml:handshake", 1, 300)]
    [InlineData("forge:tier_sorting", 2, 0)]
    public void ReadsTheChannelAndIdOfAnyMessage(string channel, int id, int length)
    {
        Assert.Equal(new LoginWrapperMessage(channel, id), LoginWrapperMessage.Read(Wrap(channel, id, new byte[length])));
    }

    [Fact]
    public void RejectsPayloadsWithMoreThanOneMessage()
    {
        byte[] payload = [.. Wrap("fml:handshake", 1, [1, 2, 3]), 0];

        Assert.Throws<InvalidDataException>(() => LoginWrapperMessage.Read(payload));
    }

    private static byte[] Wrap(string channel, int id, byte[] data)
    {
        var bytes = new byte[data.Length + 64];
        var writer = new BufferSpan(bytes);
        writer.WriteString(channel);
        writer.WriteVarInt(id.VarIntSize() + data.Length);
        writer.WriteVarInt(id);
        writer.Write(data);

        return bytes[..writer.Position];
    }
}
