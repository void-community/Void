using System;
using System.Collections.Generic;
using System.Linq;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Builder;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Network;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.ModsSupport.CrossStitch;
using Xunit;

namespace Void.UnitTests.Commands;

public class CrossStitchArgumentTests
{
    public CrossStitchArgumentTests()
    {
        ArgumentSerializerRegistry.Register(CrossStitchService.ModArgumentMapping, typeof(CrossStitchModArgumentType), CrossStitchModArgumentSerializer.Instance);
    }

    [Theory]
    [InlineData(-1, new byte[0])] // A type registered by class only, such as MineColonies' DeleteBuildingsArgumentType wrapped by PCF
    [InlineData(52, new byte[] { 1, 2, 3 })]
    public void UnwrapsToTheBackendParserIdAsRead(int parserId, byte[] properties)
    {
        var reader = new BufferSpan(WrappedArgument(parserId, properties));
        var argumentType = ArgumentSerializerRegistry.Deserialize(ref reader, ProtocolVersion.MINECRAFT_1_21);

        var output = new byte[64];
        var writer = new BufferSpan(output);
        ArgumentSerializerRegistry.Serialize(ref writer, argumentType, ProtocolVersion.MINECRAFT_1_21);

        // The client receives exactly what the backend would send on a direct connection: its parser ID and raw properties.
        var expected = new byte[64];
        var expectedWriter = new BufferSpan(expected);
        expectedWriter.WriteVarInt(parserId);
        expectedWriter.Write(properties);

        Assert.Equal(expected[..expectedWriter.Position], output[..writer.Position]);
    }

    [Fact]
    public void RemovesOnlyArgumentsThatCannotBeEncoded()
    {
        var reader = new BufferSpan(WrappedArgument(-1, []));
        var unregisteredArgument = ArgumentSerializerRegistry.Deserialize(ref reader, ProtocolVersion.MINECRAFT_1_21);
        var root = new RootCommandNode();
        root.AddChild(LiteralArgumentBuilder.Create("colony").Then(RequiredArgumentBuilder.Create("buildings", unregisteredArgument)).Build());
        root.AddChild(LiteralArgumentBuilder.Create("broken").Then(RequiredArgumentBuilder.Create("value", new UnencodableArgumentType())).Build());

        var removed = CommandsPacket.RemoveUnserializableArguments(root, ProtocolVersion.MINECRAFT_1_21);

        Assert.Equal("value", Assert.Single(removed).Node.Name);
        Assert.Empty(root.GetChild("broken").Children);
        Assert.Equal("buildings", Assert.Single(root.GetChild("colony").Children).Name);
    }

    private static byte[] WrappedArgument(int parserId, byte[] properties)
    {
        var bytes = new byte[64];
        var writer = new BufferSpan(bytes);
        writer.WriteVarInt(CrossStitchService.ModArgumentMapping.VersionParserIdMapping[ProtocolVersion.MINECRAFT_1_21]);
        writer.WriteVarInt(parserId);
        writer.WriteByteArray(properties);

        return bytes[..writer.Position];
    }

    private sealed class UnencodableArgumentType : IArgumentType
    {
        public IEnumerable<string> Examples => Enumerable.Empty<string>();

        public IArgumentValue Parse(StringReader reader) => throw new NotSupportedException();
    }
}
