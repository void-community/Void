using System.Buffers;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Exceptions;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Builder;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;

namespace Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

public class CommandsPacket : IMinecraftClientboundPacket<CommandsPacket>
{
    #region Protocol Constants (https://minecraft.wiki/w/Java_Edition_protocol/Command_data#Node_Format)

    [Flags]
    public enum ProtocolCommandNodeType : byte
    {
        Root = 0x00,
        Literal = 0x01,
        Argument = 0x02
    }

    [Flags]
    private enum ProtocolCommandNodeFlags : byte
    {
        Default = ProtocolCommandNodeType.Root,
        NodeType = 0x03,
        IsExecutable = 0x04,
        HasRedirect = 0x08,
        HasSuggestionsType = 0x10,
        IsRestricted = 0x20
    }

    #endregion

    private static readonly CommandExecutor ExecuteNothing = (context, cancellationToken) => ValueTask.FromResult(0);
    private static readonly CommandRequirement RequireNothing = (context, cancellationToken) => ValueTask.FromResult(true);

    // Trees of large modpacks pass hundreds of kilobytes, while no packet may exceed the protocol's 8 MiB.
    private const int InitialNodesBufferSize = 128 * 1024;
    private const int MaximumNodesBufferSize = 8 * 1024 * 1024;

    public RootCommandNode? RootNode { get; set; }
    public Memory<byte> FallbackNode { get; set; }
    public Exception? FallbackReason { get; set; }

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (RootNode is null)
        {
            buffer.Write(FallbackNode.Span);
            return;
        }

        var indices = new Dictionary<CommandNode, int>(ReferenceEqualityComparer.Instance);
        NumberBreadthFirst([RootNode], indices);

        buffer.WriteVarInt(indices.Count);
        buffer.Write(SerializeNodes(indices.Keys, indices, protocolVersion));
        buffer.WriteVarInt(indices[RootNode]);
    }

    public static CommandsPacket Decode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        var start = buffer.Position;
        var span = buffer.CopyAsBufferSpan(read: true);

        try
        {
            var commands = span.ReadVarInt();
            var nodes = new ProtocolCommandNode[commands];

            for (int i = 0; i < commands; i++)
                nodes[i] = DeserializeNode(ref span, i, protocolVersion);

            var rootIndex = span.ReadVarInt();
            return new CommandsPacket { RootNode = Build(nodes, rootIndex) };
        }
        catch (Exception exception)
        {
            return DecodeWithUnknownArguments(buffer.DumpBytes()[(int)start..].ToArray(), protocolVersion, exception);
        }
    }

    /// <summary>
    /// Adds commands to a command tree the proxy couldn't read by rewriting only its root. Servers number the root first and write it with no
    /// name or argument type, so every other node stays byte for byte as the server wrote it, and the added commands are numbered after them.
    /// </summary>
    /// <param name="tree">The command tree as the server wrote it, from its node count to its root index.</param>
    /// <param name="commands">The root whose commands are added; a redirect to it leads to the tree's root.</param>
    /// <param name="protocolVersion">The protocol version the tree was written for.</param>
    /// <returns>The tree with the commands added, or <see langword="null"/> when the tree doesn't start with its root.</returns>
    public static byte[]? Splice(ReadOnlySpan<byte> tree, RootCommandNode commands, ProtocolVersion protocolVersion)
    {
        var copy = tree.ToArray();
        int count, serverNodesStart;
        byte rootFlags;
        ReadOnlyMemory<int> rootChildren;

        try
        {
            var reader = new BufferSpan(copy);
            count = reader.ReadVarInt();
            rootFlags = reader.ReadUnsignedByte();
            rootChildren = reader.ReadVarIntArray();
            serverNodesStart = reader.Position;

            // The tree's last byte is its root index, which is 0 only when the root is the first node, and follows the root's own bytes.
            if ((rootFlags & (byte)ProtocolCommandNodeFlags.NodeType) != (byte)ProtocolCommandNodeType.Root || (rootFlags & (byte)ProtocolCommandNodeFlags.HasRedirect) != 0 || copy[^1] != 0 || serverNodesStart >= copy.Length)
                return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        var indices = new Dictionary<CommandNode, int>(ReferenceEqualityComparer.Instance) { [commands] = 0 };
        NumberBreadthFirst(commands.Children, indices, firstIndex: count);

        var addedNodes = SerializeNodes(indices.Keys.Where(node => node != commands), indices, protocolVersion);
        var output = new byte[copy.Length + addedNodes.Length + 32 + 5 * commands.Children.Count()];
        var writer = new BufferSpan(output);

        writer.WriteVarInt(count + indices.Count - 1);
        writer.WriteUnsignedByte(rootFlags);
        writer.WriteVarInt(rootChildren.Length + commands.Children.Count());

        foreach (var child in rootChildren.Span)
            writer.WriteVarInt(child);

        foreach (var child in commands.Children)
            writer.WriteVarInt(indices[child]);

        writer.Write(copy.AsSpan(serverNodesStart, copy.Length - serverNodesStart - 1));
        writer.Write(addedNodes);
        writer.WriteVarInt(0);

        return output[..writer.Position];
    }

    /// <summary>
    /// Reads a tree whose arguments include types the proxy doesn't know, such as ones mods register, keeping their properties as the server
    /// wrote them, or keeps the tree as the server wrote it when no layout of its nodes fits.
    /// </summary>
    private static CommandsPacket DecodeWithUnknownArguments(byte[] tree, ProtocolVersion protocolVersion, Exception failure)
    {
        try
        {
            if (CommandTreeLayout.Find(tree, protocolVersion) is { } layout)
            {
                var span = new BufferSpan(tree);
                var nodes = new ProtocolCommandNode[layout.Length];

                for (var i = 0; i < layout.Length; i++)
                {
                    span.Position = layout[i].Start;
                    nodes[i] = DeserializeNode(ref span, i, protocolVersion, layout[i].HasUnknownArgument ? layout[i].PropertiesLength : null);
                }

                return new CommandsPacket { RootNode = Build(nodes, rootIndex: 0) };
            }

            failure = new InvalidDataException("No layout of the command tree's nodes fits its argument types the proxy doesn't know.", failure);
        }
        catch (Exception exception)
        {
            failure = new AggregateException(failure, exception);
        }

        return new CommandsPacket { FallbackNode = tree, FallbackReason = failure };
    }

    private static RootCommandNode Build(ProtocolCommandNode[] nodes, int rootIndex)
    {
        var queue = new Queue<ProtocolCommandNode>(nodes);

        while (queue.Count > 0)
        {
            var builtAnyNode = false;
            var unbuiltProtocolCommandNodes = new List<ProtocolCommandNode>(queue.Count);

            while (queue.TryDequeue(out var protocolCommandNode))
            {
                if (protocolCommandNode.TryBuild(nodes))
                {
                    builtAnyNode = true;
                    continue;
                }

                unbuiltProtocolCommandNodes.Add(protocolCommandNode);
            }

            if (!builtAnyNode)
                throw new InvalidOperationException($"Broken command node graph detected; unable to build any more nodes, {unbuiltProtocolCommandNodes.Count} is left unbuilt.");

            foreach (var node in unbuiltProtocolCommandNodes)
                queue.Enqueue(node);
        }

        return nodes[rootIndex].Built as RootCommandNode ?? throw new InvalidDataException("Root node is not a instance of root command node!");
    }

    /// <summary>Numbers nodes in the order a breadth-first walk first meets them, children first and then the redirect, as servers do.</summary>
    private static void NumberBreadthFirst(IEnumerable<CommandNode> firstNodes, Dictionary<CommandNode, int> indices, int firstIndex = 0)
    {
        var queue = new Queue<CommandNode>(firstNodes);
        var nextIndex = firstIndex;

        while (queue.TryDequeue(out var commandNode))
        {
            if (!indices.TryAdd(commandNode, nextIndex))
                continue;

            nextIndex++;

            foreach (var childCommandNode in commandNode.Children)
                queue.Enqueue(childCommandNode);

            if (commandNode.RedirectTarget is not null)
                queue.Enqueue(commandNode.RedirectTarget);
        }
    }

    private static byte[] SerializeNodes(IEnumerable<CommandNode> nodes, Dictionary<CommandNode, int> indices, ProtocolVersion protocolVersion)
    {
        for (var size = InitialNodesBufferSize; size <= MaximumNodesBufferSize; size *= 2)
        {
            var output = ArrayPool<byte>.Shared.Rent(size);
            var span = new BufferSpan(output);

            try
            {
                foreach (var node in nodes)
                    SerializeNode(node, ref span, indices, protocolVersion);

                return output[..span.Position];
            }
            catch (EndOfBufferException)
            {
                // The nodes need a bigger buffer.
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(output);
            }
        }

        throw new InvalidOperationException($"Command tree nodes don't fit in {MaximumNodesBufferSize} bytes.");
    }

    /// <summary>
    /// Removes argument nodes whose argument type can't be written for <paramref name="protocolVersion"/>, so an argument the
    /// proxy can't encode costs only its command branch instead of the player's connection.
    /// </summary>
    /// <param name="rootNode">The command tree to prune.</param>
    /// <param name="protocolVersion">The protocol version the tree will be encoded for.</param>
    /// <returns>Each removed node with the reason it can't be written.</returns>
    public static List<(ArgumentCommandNode Node, Exception Reason)> RemoveUnserializableArguments(CommandNode rootNode, ProtocolVersion protocolVersion)
    {
        var removed = new List<(ArgumentCommandNode Node, Exception Reason)>();
        var failures = new Dictionary<CommandNode, Exception?>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<CommandNode>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<CommandNode>([rootNode]);
        var scratch = new byte[128 * 1024];

        while (queue.TryDequeue(out var commandNode))
        {
            if (!visited.Add(commandNode))
                continue;

            foreach (var childCommandNode in commandNode.Children.ToArray())
            {
                if (childCommandNode is ArgumentCommandNode argumentCommandNode && FindSerializationFailure(argumentCommandNode) is not null)
                {
                    commandNode.RemoveChild(argumentCommandNode.Name);
                    continue;
                }

                queue.Enqueue(childCommandNode);
            }

            if (commandNode.RedirectTarget is not null)
                queue.Enqueue(commandNode.RedirectTarget);
        }

        return removed;

        Exception? FindSerializationFailure(ArgumentCommandNode argumentCommandNode)
        {
            if (failures.TryGetValue(argumentCommandNode, out var knownFailure))
                return knownFailure;

            Exception? failure = null;

            try
            {
                var buffer = new BufferSpan(scratch);
                ArgumentSerializerRegistry.Serialize(ref buffer, argumentCommandNode.Type, protocolVersion);
            }
            catch (Exception exception)
            {
                failure = exception;
                removed.Add((argumentCommandNode, exception));
            }

            failures[argumentCommandNode] = failure;
            return failure;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private static void SerializeNode(CommandNode commandNode, ref BufferSpan buffer, Dictionary<CommandNode, int> indices, ProtocolVersion protocolVersion)
    {
        var flags = commandNode switch
        {
            RootCommandNode => (ProtocolCommandNodeFlags)ProtocolCommandNodeType.Root,
            LiteralCommandNode => (ProtocolCommandNodeFlags)ProtocolCommandNodeType.Literal,
            ArgumentCommandNode => (ProtocolCommandNodeFlags)ProtocolCommandNodeType.Argument,
            _ => throw new ArgumentException($"Unexpected node type {commandNode.GetType()}")
        };

        if (commandNode.Executor is not null)
            flags |= ProtocolCommandNodeFlags.IsExecutable;

        if (commandNode.RedirectTarget is not null)
            flags |= ProtocolCommandNodeFlags.HasRedirect;

        if (commandNode is ArgumentCommandNode { CustomSuggestions: not null })
            flags |= ProtocolCommandNodeFlags.HasSuggestionsType;

        if (commandNode.Requirement == RequireNothing)
            flags |= ProtocolCommandNodeFlags.IsRestricted;

        buffer.WriteUnsignedByte((byte)flags);

        buffer.WriteVarInt(commandNode.Children.Count());
        foreach (var childCommandNode in commandNode.Children)
            buffer.WriteVarInt(indices[childCommandNode]);

        if (commandNode.RedirectTarget is { } redirectCommandNode)
            buffer.WriteVarInt(indices[redirectCommandNode]);

        switch (commandNode)
        {
            case LiteralCommandNode literalCommandNode:
                SerializeLiteralNode(ref buffer, literalCommandNode);
                break;
            case ArgumentCommandNode argumentCommandNode:
                SerializeArgumentNode(ref buffer, argumentCommandNode);
                break;
        }
        
        return;

        void SerializeLiteralNode(ref BufferSpan buffer, LiteralCommandNode literalCommandNode)
        {
            buffer.WriteString(literalCommandNode.Name);
        }

        void SerializeArgumentNode(ref BufferSpan buffer, ArgumentCommandNode argumentCommandNode)
        {
            buffer.WriteString(argumentCommandNode.Name);
            ArgumentSerializerRegistry.Serialize(ref buffer, argumentCommandNode.Type, protocolVersion);

            if (argumentCommandNode.CustomSuggestions is null)
                return;

            // Suggestions the server named are named again, and any other suggestions are asked of the server, which the proxy answers.
            var name = argumentCommandNode.CustomSuggestions.Target is NamedSuggestions namedSuggestions ? namedSuggestions.Name : "minecraft:ask_server";

            buffer.WriteString(name);
        }
    }

    /// <param name="unknownPropertiesLength">For an argument of a type the proxy doesn't know, how long its properties are.</param>
    private static ProtocolCommandNode DeserializeNode(ref BufferSpan buffer, int index, ProtocolVersion protocolVersion, int? unknownPropertiesLength = null)
    {
        var flags = (ProtocolCommandNodeFlags)buffer.ReadUnsignedByte();
        var childrenIndices = buffer.ReadVarIntArray();
        var redirectToIndex = flags.HasFlag(ProtocolCommandNodeFlags.HasRedirect) ? buffer.ReadVarInt() : -1;

        var nodeType = (ProtocolCommandNodeType)(flags & ProtocolCommandNodeFlags.NodeType);

        return nodeType switch
        {
            ProtocolCommandNodeType.Root => DeserializeRootNode(),
            ProtocolCommandNodeType.Literal => DeserializeLiteralNode(ref buffer),
            ProtocolCommandNodeType.Argument => DeserializeArgumentNode(ref buffer),
            _ => throw new InvalidDataException($"Unexpected node type {nodeType}"),
        };

        ProtocolCommandNode DeserializeRootNode()
        {
            return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex);
        }

        ProtocolCommandNode DeserializeLiteralNode(ref BufferSpan buffer)
        {
            var name = LiteralArgumentBuilder.Create(buffer.ReadString());
            return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex, name);
        }

        ProtocolCommandNode DeserializeArgumentNode(ref BufferSpan buffer)
        {
            var name = buffer.ReadString();
            var argumentType = unknownPropertiesLength is { } length ? DeserializeUnknownArgument(ref buffer, length) : ArgumentSerializerRegistry.Deserialize(ref buffer, protocolVersion);
            var argumentBuilder = RequiredArgumentBuilder.Create(name, argumentType);

            if (!flags.HasFlag(ProtocolCommandNodeFlags.HasSuggestionsType))
                return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex, argumentBuilder);

            if (argumentBuilder is null)
                throw new InvalidDataException($"Argument node has suggestions type but deserialized builder is not a {nameof(RequiredArgumentBuilder)}.");

            argumentBuilder.Suggests(new NamedSuggestions(buffer.ReadString()).SuggestAsync);

            return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex, argumentBuilder);
        }

        UnknownArgumentType DeserializeUnknownArgument(ref BufferSpan buffer, int length)
        {
            ArgumentSerializerRegistry.TryDecodeParserMapping(ref buffer, protocolVersion, out var mapping);
            return new UnknownArgumentType(mapping, buffer.Read(length).ToArray());
        }
    }

    /// <summary>
    /// Suggestions a server named for an argument, such as minecraft:available_sounds, which clients list themselves. Each node gets its own
    /// provider, so its name goes back to the client unchanged.
    /// </summary>
    private sealed record NamedSuggestions(string Name)
    {
        public ValueTask<Suggestions> SuggestAsync(CommandContext context, SuggestionsBuilder builder, CancellationToken cancellationToken)
        {
            return builder.BuildAsync(cancellationToken);
        }
    }

    private record ProtocolCommandNode(int Index, ProtocolCommandNodeType Type, ProtocolCommandNodeFlags Flags, ReadOnlyMemory<int> Children, int RedirectTo, ArgumentBuilder? ArgumentBuilder = null)
    {
        private bool _validated;

        public CommandNode? Built { get; private set; }

        public bool TryBuild(ProtocolCommandNode[] nodes)
        {
            if (!_validated)
                Validate(nodes);

            if (Built is null)
            {
                if (Type is ProtocolCommandNodeType.Root)
                {
                    Built = new RootCommandNode();
                }
                else
                {
                    if (ArgumentBuilder is null)
                        throw new InvalidOperationException($"{Type} node has no argument builder.");

                    if (RedirectTo is not -1)
                    {
                        var redirect = nodes[RedirectTo];

                        if (redirect.Built is not null)
                            ArgumentBuilder.Redirect(redirect.Built);
                        else
                            return false; // Target node is not yet built
                    }

                    if (Flags.HasFlag(ProtocolCommandNodeFlags.IsExecutable))
                        ArgumentBuilder.Executes(ExecuteNothing);

                    if (Flags.HasFlag(ProtocolCommandNodeFlags.IsRestricted))
                        ArgumentBuilder.Requires(RequireNothing);

                    Built = ArgumentBuilder.Build();
                }
            }

            var childrenIndices = Children.Span;

            foreach (var childIndex in childrenIndices)
            {
                var childCommandNode = nodes[childIndex].Built;

                if (childCommandNode is null)
                    return false; // The child node is not yet built

                if (childCommandNode is RootCommandNode)
                    throw new InvalidOperationException("A root command node cannot be a child of another node.");

                Built.AddChild(childCommandNode);
            }

            return true;
        }

        private void Validate(ProtocolCommandNode[] nodes)
        {
            foreach (var child in Children.Span)
                if (child < 0 || child >= nodes.Length)
                    throw new InvalidOperationException($"Node points to non-existent index {child}.");

            if (RedirectTo is not -1)
                if (RedirectTo < 0 || RedirectTo >= nodes.Length)
                    throw new InvalidOperationException($"Redirect node points to non-existent index {RedirectTo}.");

            _validated = true;
        }
    }
}
