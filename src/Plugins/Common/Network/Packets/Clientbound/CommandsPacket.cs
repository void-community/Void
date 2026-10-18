using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Builder;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Proxy.Plugins.Common.Extensions;

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
    private static readonly Dictionary<string, SuggestionProvider> SuggestionProvidersNames = [];

    public RootCommandNode? RootNode { get; set; }
    public Memory<byte> FallbackNode { get; set; }
    public Exception? FallbackReason { get; set; }

    public void Encode(ref MinecraftBuffer buffer, ProtocolVersion protocolVersion)
    {
        if (RootNode is not null)
        {
            var queue = new Queue<CommandNode>([RootNode]);
            var indices = new Dictionary<CommandNode, int>(ReferenceEqualityComparer.Instance);

            while (queue.Count > 0)
            {
                var commandNode = queue.Dequeue();

                if (!indices.TryAdd(commandNode, indices.Count))
                    continue;

                foreach (var childCommandNode in commandNode.Children)
                    queue.Enqueue(childCommandNode);

                if (commandNode.RedirectTarget is not null)
                    queue.Enqueue(commandNode.RedirectTarget);
            }

            buffer.WriteVarInt(indices.Count);

            var span = new BufferSpan(stackalloc byte[128 * 1024]);
            foreach (var node in indices.Keys)
                SerializeNode(node, ref span, indices, protocolVersion);

            buffer.Write(span.Access(0, span.Position));
            buffer.WriteVarInt(indices[RootNode]);
        }
        else
        {
            buffer.Write(FallbackNode.Span);
        }
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
                    throw new InvalidOperationException($"Broken command node graph detected; unable to build any more nodes, {queue.Count(protocolCommandNode => protocolCommandNode.Built is null)} is left unbuilt.");

                foreach (var node in unbuiltProtocolCommandNodes)
                    queue.Enqueue(node);
            }
            
            var rootIndex = span.ReadVarInt();
            return new CommandsPacket { RootNode = nodes[rootIndex].Built as RootCommandNode ?? throw new InvalidDataException("Root node is not a instance of root command node!"), };
        }
        catch (Exception exception)
        {
            return new CommandsPacket { FallbackNode = buffer.DumpBytes()[(int)start..].ToArray(), FallbackReason = exception };
        }
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

            var provider = argumentCommandNode.CustomSuggestions;
            var name = "minecraft:ask_server";

            if (SuggestionProvidersNames.TryGetKey(provider, out var foundName))
                name = foundName;

            buffer.WriteString(name);
        }
    }

    private static ProtocolCommandNode DeserializeNode(ref BufferSpan buffer, int index, ProtocolVersion protocolVersion)
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
            var argumentType = ArgumentSerializerRegistry.Deserialize(ref buffer, protocolVersion);
            var argumentBuilder = RequiredArgumentBuilder.Create(name, argumentType);

            if (!flags.HasFlag(ProtocolCommandNodeFlags.HasSuggestionsType))
                return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex, argumentBuilder);

            if (argumentBuilder is null)
                throw new InvalidDataException($"Argument node has suggestions type but deserialized builder is not a {nameof(RequiredArgumentBuilder)}.");

            var providerName = buffer.ReadString();
            var provider = new SuggestionProvider((context, builder, cancellationToken) => builder.BuildAsync(cancellationToken));

            _ = SuggestionProvidersNames.TryAdd(providerName, provider);
            argumentBuilder.Suggests(provider);

            return new ProtocolCommandNode(index, nodeType, flags, childrenIndices, redirectToIndex, argumentBuilder);
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
