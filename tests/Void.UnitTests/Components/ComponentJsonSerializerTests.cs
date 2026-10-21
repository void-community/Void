using System;
using System.Linq;
using System.Text.Json.Nodes;
using Void.Minecraft.Buffers;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Components.Text.Properties.Content;
using Void.Minecraft.Components.Text.Serializers;
using Void.Minecraft.Nbt.Tags;
using Void.Minecraft.Network;
using Void.Minecraft.Profiles;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Xunit;

namespace Void.UnitTests.Components;

public class ComponentJsonSerializerTests
{
    // Servers from 1.16 to 1.18 send chat as a translation of the player's name, a component, and the message, a string.
    private const string PlayerChat = """{"translate":"chat.type.text","with":[{"text":"Player","insertion":"Player"},"hello"]}""";

    [Fact]
    public void ReadsTranslationArgumentsThatMixStringsAndComponents()
    {
        var component = ComponentJsonSerializer.Deserialize(PlayerChat);

        var content = Assert.IsType<TranslatableContent>(component.Content);
        var arguments = content.With!.ToArray();

        Assert.Equal("Player", Assert.IsType<TextContent>(arguments[0].Content).Value);
        Assert.Equal("hello", Assert.IsType<NbtString>(Assert.IsType<LiteralContent>(arguments[1].Content).Value).Value);
    }

    [Theory]
    [InlineData(PlayerChat)]
    [InlineData("""{"text":"","extra":[{"text":"a","color":"red"},"b",5]}""")]
    [InlineData("""{"translate":"outer","with":[{"translate":"inner","with":["x",{"text":"y"}]},"z"]}""")]
    public void WritesArraysOfMixedTypesBackAsTheServerSentThem(string json)
    {
        var component = ComponentJsonSerializer.Deserialize(json);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), ComponentJsonSerializer.Serialize(component)));
    }

    [Fact]
    public void ReadsEmptyArrays()
    {
        Assert.Equal("hello", ComponentJsonSerializer.Deserialize("""{"text":"hello","extra":[]}""").AsText);
    }

    [Fact]
    public void RelaysChatWhoseArgumentsMixStringsAndComponents()
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_18_2;
        var sender = Uuid.CreateOfflinePlayer("Player");
        var input = new byte[1024];
        var writer = new MinecraftBuffer(input.AsSpan());
        writer.WriteString(PlayerChat);
        writer.WriteUnsignedByte(0);
        writer.WriteUuid(sender);

        var reader = new MinecraftBuffer(new ReadOnlySpan<byte>(input, 0, (int)writer.Position));
        var packet = ChatMessagePacket.Decode(ref reader, protocolVersion);

        var output = new byte[1024];
        var encoder = new MinecraftBuffer(output.AsSpan());
        packet.Encode(ref encoder, protocolVersion);

        var relayed = new MinecraftBuffer(new ReadOnlySpan<byte>(output, 0, (int)encoder.Position));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(PlayerChat), JsonNode.Parse(relayed.ReadString())));
        Assert.Equal(0, relayed.ReadUnsignedByte());
        Assert.Equal(sender, relayed.ReadUuid());
    }
}
