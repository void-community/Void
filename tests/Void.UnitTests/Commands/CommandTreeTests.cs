using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Builder;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Network;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Xunit;

namespace Void.UnitTests.Commands;

/// <summary>Command trees operators got from real modded servers, whose argument types include ones the proxy doesn't know.</summary>
public class CommandTreeTests
{
    private static readonly CommandExecutor ExecuteNothing = (context, cancellationToken) => ValueTask.FromResult(0);

    private static readonly (string Fixture, ProtocolVersion ProtocolVersion, string CommandWithUnknownArguments)[] Fixtures =
    [
        // Forge 1.16.5 names its argument types, such as forge:modid and forge:enum.
        ("forge-1.16.5", ProtocolVersion.MINECRAFT_1_16_4, "config"),
        ("forge-1.20.1", ProtocolVersion.MINECRAFT_1_20, "config"),
        ("neoforge-1.21.1", ProtocolVersion.MINECRAFT_1_21, "config"),
        // All the Mods 10 8.3 has 148 arguments of types mods register, some named with spaces.
        ("atm10-8.3", ProtocolVersion.MINECRAFT_1_21, "minecolonies")
    ];

    public static TheoryData<string, int, string> ModdedTrees => [.. Fixtures.Select(fixture => (fixture.Fixture, fixture.ProtocolVersion.Value, fixture.CommandWithUnknownArguments))];

    public static TheoryData<string, int> ModdedTreeFiles => [.. Fixtures.Select(fixture => (fixture.Fixture, fixture.ProtocolVersion.Value))];

    [Theory]
    [MemberData(nameof(ModdedTrees))]
    public void ReadsCommandsWithArgumentTypesTheProxyDoesNotKnow(string fixture, int protocol, string commandWithUnknownArguments)
    {
        var protocolVersion = ProtocolVersion.Range().First(version => version.Value == protocol);
        var packet = Decode(ReadFixture(fixture), protocolVersion);

        Assert.NotNull(packet.RootNode);
        Assert.Contains("help", packet.RootNode.Children.Select(command => command.Name));

        var availableCommands = new AvailableCommandsEvent(null!, null!, packet.RootNode);
        Assert.Contains(commandWithUnknownArguments, availableCommands.CommandsWithUnknownArguments.Select(command => command.Name));
        Assert.DoesNotContain("help", availableCommands.CommandsWithUnknownArguments.Select(command => command.Name));
    }

    [Theory]
    [MemberData(nameof(ModdedTreeFiles))]
    public void WritesTheTreeBackAsTheServerSentIt(string fixture, int protocol)
    {
        var protocolVersion = ProtocolVersion.Range().First(version => version.Value == protocol);
        var tree = ReadFixture(fixture);

        Assert.Equal(tree, Encode(Decode(tree, protocolVersion), protocolVersion));
    }

    [Theory]
    [MemberData(nameof(ModdedTrees))]
    public void AddsCommandsInsideCommandsWithUnknownArguments(string fixture, int protocol, string commandWithUnknownArguments)
    {
        var protocolVersion = ProtocolVersion.Range().First(version => version.Value == protocol);
        var packet = Decode(ReadFixture(fixture), protocolVersion);
        var serverCommands = packet.RootNode!.Children.Select(command => command.Name).ToArray();

        packet.RootNode.AddChild(VoidCommand("voidtest"));
        packet.RootNode.GetChild(commandWithUnknownArguments).AddChild(VoidCommand("voidchild"));

        var reread = Decode(Encode(packet, protocolVersion), protocolVersion).RootNode;

        Assert.NotNull(reread);
        Assert.Equal([.. serverCommands, "voidtest"], reread.Children.Select(command => command.Name));
        Assert.Contains("voidchild", reread.GetChild(commandWithUnknownArguments).Children.Select(child => child.Name));
        Assert.Contains(commandWithUnknownArguments, new AvailableCommandsEvent(null!, null!, reread).CommandsWithUnknownArguments.Select(command => command.Name));
    }

    [Theory]
    [MemberData(nameof(ModdedTreeFiles))]
    public void SplicesCommandsKeepingEveryServerNode(string fixture, int protocol)
    {
        var protocolVersion = ProtocolVersion.Range().First(version => version.Value == protocol);
        var tree = ReadFixture(fixture);

        var commands = new RootCommandNode();
        commands.AddChild(VoidCommand("voidtest"));

        var spliced = CommandsPacket.Splice(tree, commands, protocolVersion);

        Assert.NotNull(spliced);

        var (count, children, serverNodesStart) = ReadRoot(tree);
        var (splicedCount, splicedChildren, splicedServerNodesStart) = ReadRoot(spliced);
        var serverNodes = tree.AsSpan(serverNodesStart, tree.Length - serverNodesStart - 1);

        // The command and its argument are numbered after the server's nodes, which stay as the server wrote them.
        Assert.Equal(count + 2, splicedCount);
        Assert.Equal([.. children, count], splicedChildren);
        Assert.True(serverNodes.SequenceEqual(spliced.AsSpan(splicedServerNodesStart, serverNodes.Length)));
        Assert.Equal(0, spliced[^1]);
    }

    [Fact]
    public void SplicesCommandsIntoATreeVoidReads()
    {
        var server = new RootCommandNode();
        server.AddChild(VoidCommand("list"));
        server.AddChild(LiteralArgumentBuilder.Create("help").Executes(ExecuteNothing).Build());
        var tree = Encode(new CommandsPacket { RootNode = server }, ProtocolVersion.MINECRAFT_1_21);

        var commands = new RootCommandNode();
        commands.AddChild(VoidCommand("voidtest"));

        var spliced = CommandsPacket.Splice(tree, commands, ProtocolVersion.MINECRAFT_1_21);
        var root = Decode(spliced!, ProtocolVersion.MINECRAFT_1_21).RootNode;

        Assert.NotNull(root);
        Assert.Equal(["list", "help", "voidtest"], root.Children.Select(command => command.Name));
        Assert.Equal("target", Assert.Single(root.GetChild("voidtest").Children).Name);
    }

    [Fact]
    public void DoesNotSpliceATreeWhoseRootIsNotFirst()
    {
        // Two nodes: a literal first, then the root pointing at it.
        byte[] tree = [0x02, 0x01, 0x00, 0x04, (byte)'h', (byte)'e', (byte)'l', (byte)'p', 0x00, 0x01, 0x00, 0x01];

        Assert.Null(CommandsPacket.Splice(tree, new RootCommandNode(), ProtocolVersion.MINECRAFT_1_21));
    }

    [Fact]
    public void KeepsATreeAsSentWhenItCannotBeRead()
    {
        var tree = ReadFixture("atm10-8.3");

        // Cut the tree short, so no layout of its nodes ends with the root index.
        var damaged = tree[..^100];
        var packet = Decode(damaged, ProtocolVersion.MINECRAFT_1_21);

        Assert.Null(packet.RootNode);
        Assert.NotNull(packet.FallbackReason);
        Assert.Equal(damaged, packet.FallbackNode.ToArray());
    }

    [Fact]
    public void GivesUpQuicklyOnATreeNoLayoutFits()
    {
        // Every one of these arguments has lengths to try, and none of them makes the wrong root index fit.
        var tree = Encode(TreeWithUnknownArguments(2000), ProtocolVersion.MINECRAFT_1_21);
        tree[^1] = 1;

        var stopwatch = Stopwatch.StartNew();
        var packet = Decode(tree, ProtocolVersion.MINECRAFT_1_21);

        Assert.Null(packet.RootNode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Giving up took {stopwatch.Elapsed}");
    }

    [Fact]
    public void KeepsATreeWithTooManyUnknownArgumentsAsSent()
    {
        // Each unknown argument is read one call deeper, so a tree with more of them than the search allows stays as the server sent it.
        var tree = Encode(TreeWithUnknownArguments(3000), ProtocolVersion.MINECRAFT_1_21);
        var packet = Decode(tree, ProtocolVersion.MINECRAFT_1_21);

        Assert.Null(packet.RootNode);
        Assert.Equal(tree, packet.FallbackNode.ToArray());
    }

    [Fact]
    public void EncodesTreesLargerThan128KiB()
    {
        var root = new RootCommandNode();

        for (var i = 0; i < 5000; i++)
            root.AddChild(LiteralArgumentBuilder.Create($"command-with-a-long-name-for-a-large-modpack-number-{i}").Executes(ExecuteNothing).Build());

        var encoded = Encode(new CommandsPacket { RootNode = root }, ProtocolVersion.MINECRAFT_1_21);

        Assert.True(encoded.Length > 128 * 1024);
        Assert.Equal(5000, Decode(encoded, ProtocolVersion.MINECRAFT_1_21).RootNode!.Children.Count());
    }

    private static CommandNode VoidCommand(string name)
    {
        return LiteralArgumentBuilder.Create(name)
            .Then(RequiredArgumentBuilder.Create("target", StringArgumentType.Word()).Executes(ExecuteNothing))
            .Executes(ExecuteNothing)
            .Build();
    }

    private static CommandsPacket TreeWithUnknownArguments(int count)
    {
        var root = new RootCommandNode();
        var argumentType = new UnknownArgumentType(new ArgumentSerializerMapping("unknown:300", ProtocolVersion.MINECRAFT_1_21, 300), new byte[] { 4, 5 });

        for (var i = 0; i < count; i++)
            root.AddChild(LiteralArgumentBuilder.Create($"command{i}").Then(RequiredArgumentBuilder.Create("value", argumentType).Executes(ExecuteNothing)).Build());

        return new CommandsPacket { RootNode = root };
    }

    private static (int Count, int[] Children, int ServerNodesStart) ReadRoot(byte[] tree)
    {
        var reader = new BufferSpan(tree);
        var count = reader.ReadVarInt();

        // The root's flags.
        reader.ReadUnsignedByte();

        var children = reader.ReadVarIntArray().ToArray();

        return (count, children, reader.Position);
    }

    private static byte[] ReadFixture(string name)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Commands", "Fixtures", $"{name}.bin"));
    }

    private static CommandsPacket Decode(byte[] tree, ProtocolVersion protocolVersion)
    {
        var buffer = new MinecraftBuffer(new ReadOnlySpan<byte>(tree));
        return CommandsPacket.Decode(ref buffer, protocolVersion);
    }

    private static byte[] Encode(CommandsPacket packet, ProtocolVersion protocolVersion)
    {
        var output = new byte[4 * 1024 * 1024];
        var buffer = new MinecraftBuffer(output.AsSpan());
        packet.Encode(ref buffer, protocolVersion);
        return output[..(int)buffer.Position];
    }
}
