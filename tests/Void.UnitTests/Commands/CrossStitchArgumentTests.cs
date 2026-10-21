using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    private static readonly CommandExecutor ExecuteNothing = (context, cancellationToken) => ValueTask.FromResult(0);

    [Theory]
    [InlineData(-1, new byte[0])] // A type registered by class only, such as MineColonies' DeleteBuildingsArgumentType wrapped by PCF
    [InlineData(52, new byte[] { 1, 2, 3 })]
    public void UnwrapsToTheServersOwnParserId(int parserId, byte[] properties)
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_21;
        var packet = Decode(Encode(Tree(new UnknownArgumentType(CrossStitchService.ModArgumentMapping, WrappedProperties(parserId, properties))), protocolVersion), protocolVersion);

        CrossStitchService.Unwrap(packet.RootNode!, protocolVersion);

        // The player gets exactly what the server would send on a direct connection: its parser ID and properties.
        var direct = Tree(new UnknownArgumentType(new ArgumentSerializerMapping($"unknown:{parserId}", protocolVersion, parserId), properties));
        Assert.Equal(Encode(direct, protocolVersion), Encode(packet, protocolVersion));
    }

    [Fact]
    public void UnwrapsToTheServersOwnIdentifierBefore119()
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_16_4;
        var wrapped = new UnknownArgumentType(CrossStitchService.ModArgumentMapping, WrappedProperties("examplemod:building", [1, 2, 3]));
        var packet = Decode(Encode(Tree(wrapped), protocolVersion), protocolVersion);

        CrossStitchService.Unwrap(packet.RootNode!, protocolVersion);

        var direct = Tree(new UnknownArgumentType(new ArgumentSerializerMapping("examplemod:building"), new byte[] { 1, 2, 3 }));
        Assert.Equal(Encode(direct, protocolVersion), Encode(packet, protocolVersion));
    }

    [Fact]
    public void LeavesArgumentsItDidNotWrapAsTheServerSentThem()
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_21;
        var modded = new UnknownArgumentType(new ArgumentSerializerMapping("unknown:300", protocolVersion, 300), new byte[] { 4, 5 });
        var damaged = new UnknownArgumentType(CrossStitchService.ModArgumentMapping, (byte[])[.. WrappedProperties(52, [1, 2, 3]), 0]);

        foreach (var argumentType in new[] { modded, damaged })
        {
            var tree = Encode(Tree(argumentType), protocolVersion);
            var packet = Decode(tree, protocolVersion);

            CrossStitchService.Unwrap(packet.RootNode!, protocolVersion);
            Assert.Equal(tree, Encode(packet, protocolVersion));
        }
    }

    [Fact]
    public void RemovesOnlyArgumentsThatCannotBeEncoded()
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_21;
        var root = Decode(Encode(Tree(new UnknownArgumentType(CrossStitchService.ModArgumentMapping, WrappedProperties(-1, []))), protocolVersion), protocolVersion).RootNode!;
        CrossStitchService.Unwrap(root, protocolVersion);
        root.AddChild(LiteralArgumentBuilder.Create("broken").Then(RequiredArgumentBuilder.Create("value", new UnencodableArgumentType())).Build());

        var removed = CommandsPacket.RemoveUnserializableArguments(root, protocolVersion);

        Assert.Equal("value", Assert.Single(removed).Node.Name);
        Assert.Empty(root.GetChild("broken").Children);
        Assert.Equal("buildings", Assert.Single(root.GetChild("colony").Children).Name);
    }

    private static CommandsPacket Tree(IArgumentType argumentType)
    {
        var root = new RootCommandNode();
        root.AddChild(LiteralArgumentBuilder.Create("colony").Then(RequiredArgumentBuilder.Create("buildings", argumentType).Executes(ExecuteNothing)).Build());

        return new CommandsPacket { RootNode = root };
    }

    private static byte[] WrappedProperties(int parserId, byte[] properties)
    {
        var bytes = new byte[64];
        var writer = new BufferSpan(bytes);
        writer.WriteVarInt(parserId);
        writer.WriteByteArray(properties);

        return bytes[..writer.Position];
    }

    private static byte[] WrappedProperties(string identifier, byte[] properties)
    {
        var bytes = new byte[64];
        var writer = new BufferSpan(bytes);
        writer.WriteString(identifier);
        writer.WriteByteArray(properties);

        return bytes[..writer.Position];
    }

    private static CommandsPacket Decode(byte[] tree, ProtocolVersion protocolVersion)
    {
        var buffer = new MinecraftBuffer(new ReadOnlySpan<byte>(tree));
        return CommandsPacket.Decode(ref buffer, protocolVersion);
    }

    private static byte[] Encode(CommandsPacket packet, ProtocolVersion protocolVersion)
    {
        var output = new byte[64 * 1024];
        var buffer = new MinecraftBuffer(output.AsSpan());
        packet.Encode(ref buffer, protocolVersion);
        return output[..(int)buffer.Position];
    }

    private sealed class UnencodableArgumentType : IArgumentType
    {
        public IEnumerable<string> Examples => Enumerable.Empty<string>();

        public IArgumentValue Parse(StringReader reader) => throw new NotSupportedException();
    }
}
