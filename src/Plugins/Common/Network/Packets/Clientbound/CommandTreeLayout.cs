using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Network;

namespace Void.Proxy.Plugins.Common.Network.Packets.Clientbound;

/// <summary>Where one node of a command tree lies, and for an argument of an unknown type, where its properties lie.</summary>
/// <param name="Start">The node's first byte.</param>
/// <param name="End">The byte after the node.</param>
/// <param name="PropertiesStart">The first byte of an unknown argument type's properties, or -1 when the node has none.</param>
/// <param name="PropertiesEnd">The byte after an unknown argument type's properties, or -1 when the node has none.</param>
internal record CommandNodeLayout(int Start, int End, int PropertiesStart = -1, int PropertiesEnd = -1)
{
    public int[] Children { get; init; } = [];
    public int Redirect { get; init; } = -1;
    public bool HasUnknownArgument => PropertiesStart >= 0;
    public int PropertiesLength => PropertiesEnd - PropertiesStart;
}

/// <summary>
/// Finds where the nodes of a command tree lie when some arguments have types the proxy doesn't know, such as ones mods register. Their
/// properties have no length on the wire, so every length is tried, and one is kept only when all the remaining nodes and the root index then
/// read to the exact end of the tree, numbered in the breadth-first order servers write them in.
/// </summary>
internal sealed class CommandTreeLayout
{
    private const byte NodeTypeMask = 0x03, HasRedirect = 0x08, HasSuggestionsType = 0x10, KnownFlags = 0x3F;

    // Properties longer than this aren't looked for; the longest seen in modpacks are class names well under it.
    private const int MaximumPropertiesLength = 4096;

    // Each node is read about once on real trees, so this bounds only trees where no length fits.
    private const int NodeReadsPerNode = 64;
    private const int MinimumNodeReads = 100_000;

    private readonly byte[] _tree;
    private readonly ProtocolVersion _protocolVersion;
    private readonly CommandNodeLayout[] _nodes;
    private readonly int _nodesStart;
    private readonly long _nodeReadsBudget;

    // A tree that can't be read from a node and a byte on can't be read so regardless of how the earlier nodes were read.
    private readonly HashSet<long> _unreadable = [];
    private long _nodeReads;
    private int _orderRejections;

    private CommandTreeLayout(byte[] tree, ProtocolVersion protocolVersion)
    {
        var buffer = new BufferSpan(tree);
        var count = buffer.ReadVarInt();

        if (count <= 0 || count > tree.Length)
            throw new InvalidDataException($"Command tree has {count} nodes in {tree.Length} bytes.");

        _tree = tree;
        _protocolVersion = protocolVersion;
        _nodes = new CommandNodeLayout[count];
        _nodesStart = buffer.Position;
        _nodeReadsBudget = Math.Max(MinimumNodeReads, (long)count * NodeReadsPerNode);
    }

    /// <summary>Finds where the nodes of <paramref name="tree"/> lie.</summary>
    /// <param name="tree">The command tree, from its node count to its root index.</param>
    /// <param name="protocolVersion">The protocol version the tree was written for.</param>
    /// <returns>Each node's layout, the root being node 0, or <see langword="null"/> when no layout fits the tree.</returns>
    public static CommandNodeLayout[]? Find(byte[] tree, ProtocolVersion protocolVersion)
    {
        var layout = new CommandTreeLayout(tree, protocolVersion);
        return layout.Read(0, layout._nodesStart) ? layout._nodes : null;
    }

    private bool Read(int index, int position)
    {
        var state = ((long)index << 32) | (uint)position;

        if (_unreadable.Contains(state))
            return false;

        var orderRejections = _orderRejections;

        if (ReadFrom(index, position))
            return true;

        // Only the order check depends on how earlier nodes were read, so failures from it aren't remembered.
        if (_orderRejections == orderRejections)
            _unreadable.Add(state);

        return false;
    }

    private bool ReadFrom(int index, int position)
    {
        for (; index < _nodes.Length; index++)
        {
            if (++_nodeReads > _nodeReadsBudget)
                return false;

            if (ReadNode(index, position) is not var (node, unknownPropertiesStart))
                return false;

            if (unknownPropertiesStart < 0)
            {
                _nodes[index] = node;
                position = node.End;
                continue;
            }

            var hasSuggestionsType = (_tree[node.Start] & HasSuggestionsType) != 0;

            foreach (var length in CandidatePropertiesLengths(unknownPropertiesStart))
            {
                var propertiesEnd = unknownPropertiesStart + length;
                var end = hasSuggestionsType ? ReadIdentifierEnd(propertiesEnd, allowEmptyPath: false) : propertiesEnd;

                if (end < 0)
                    continue;

                _nodes[index] = node with { End = end, PropertiesStart = unknownPropertiesStart, PropertiesEnd = propertiesEnd };

                if (Read(index + 1, end))
                    return true;
            }

            return false;
        }

        if (!EndsWithRootIndex(position))
            return false;

        if (IsBreadthFirst())
            return true;

        _orderRejections++;
        return false;
    }

    /// <summary>No properties first, then one length-prefixed string, the shapes mods use most, then every other length.</summary>
    private IEnumerable<int> CandidatePropertiesLengths(int propertiesStart)
    {
        var maximumLength = Math.Min(MaximumPropertiesLength, _tree.Length - propertiesStart);
        var stringLength = -1;

        yield return 0;

        try
        {
            var buffer = new BufferSpan(_tree) { Position = propertiesStart };
            var length = buffer.ReadVarInt();

            if (length > 0 && buffer.Position - propertiesStart + length <= maximumLength)
                stringLength = buffer.Position - propertiesStart + length;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The properties don't start with a length.
        }

        if (stringLength > 0)
            yield return stringLength;

        for (var length = 1; length <= maximumLength; length++)
        {
            if (length != stringLength)
                yield return length;
        }
    }

    /// <summary>Reads one node; for an argument of an unknown type, returns where its properties start instead of where it ends.</summary>
    private (CommandNodeLayout Node, int UnknownPropertiesStart)? ReadNode(int index, int position)
    {
        try
        {
            var buffer = new BufferSpan(_tree) { Position = position };
            var flags = buffer.ReadUnsignedByte();
            var nodeType = flags & NodeTypeMask;

            // Servers number the root first and nodes of no other type there.
            if ((flags & ~KnownFlags) != 0 || nodeType == 3 || (nodeType == 0) != (index == 0))
                return null;

            var children = new int[buffer.ReadVarInt()];

            if (children.Length > _nodes.Length)
                return null;

            for (var i = 0; i < children.Length; i++)
            {
                children[i] = buffer.ReadVarInt();

                if (children[i] <= 0 || children[i] >= _nodes.Length)
                    return null;
            }

            var redirect = -1;

            if ((flags & HasRedirect) != 0)
            {
                redirect = buffer.ReadVarInt();

                if (nodeType == 0 || redirect < 0 || redirect >= _nodes.Length)
                    return null;
            }

            var node = new CommandNodeLayout(position, -1) { Children = children, Redirect = redirect };

            if (nodeType == 0)
                return (node with { End = buffer.Position }, -1);

            // Argument names are labels, which mods write with spaces too.
            if (!IsName(buffer.ReadString()))
                return null;

            if (nodeType == 1)
                return (node with { End = buffer.Position }, -1);

            var parserStart = buffer.Position;

            if (!ArgumentSerializerRegistry.TryDecodeParserMapping(ref buffer, _protocolVersion, out var mapping))
            {
                // Before 1.19, the type is named, and servers name a type they can't send with an empty path.
                if (_protocolVersion < ProtocolVersion.MINECRAFT_1_19 && !IsIdentifier(mapping.Identifier, allowEmptyPath: true))
                    return null;

                return (node, buffer.Position);
            }

            buffer.Position = parserStart;
            ArgumentSerializerRegistry.Deserialize(ref buffer, _protocolVersion);

            var end = (flags & HasSuggestionsType) != 0 ? ReadIdentifierEnd(buffer.Position, allowEmptyPath: false) : buffer.Position;
            return end < 0 ? null : (node with { End = end }, -1);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Any read past a wrong boundary fails somewhere in here.
            return null;
        }
    }

    private bool EndsWithRootIndex(int position)
    {
        try
        {
            var buffer = new BufferSpan(_tree) { Position = position };
            return buffer.ReadVarInt() == 0 && buffer.Position == _tree.Length;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Servers number nodes in the order a breadth-first walk from the root first meets them, children in their written order and then the
    /// redirect, so nodes read at the right boundaries are numbered that way again.
    /// </summary>
    private bool IsBreadthFirst()
    {
        var visited = new bool[_nodes.Length];
        var queue = new Queue<int>([0]);
        var next = 0;

        while (queue.TryDequeue(out var index))
        {
            if (visited[index])
                continue;

            if (index != next++)
                return false;

            visited[index] = true;

            foreach (var child in _nodes[index].Children)
                queue.Enqueue(child);

            if (_nodes[index].Redirect >= 0)
                queue.Enqueue(_nodes[index].Redirect);
        }

        return next == _nodes.Length;
    }

    private int ReadIdentifierEnd(int position, bool allowEmptyPath)
    {
        try
        {
            var buffer = new BufferSpan(_tree) { Position = position };
            return IsIdentifier(buffer.ReadString(), allowEmptyPath) ? buffer.Position : -1;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return -1;
        }
    }

    private static bool IsName(string name)
    {
        return name.Length > 0 && !name.Any(character => char.IsControl(character) || character == '�');
    }

    private static bool IsIdentifier(string identifier, bool allowEmptyPath)
    {
        var separator = identifier.IndexOf(':');

        if (separator <= 0 || (!allowEmptyPath && separator == identifier.Length - 1))
            return false;

        return identifier.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-' or '/' or ':')
               && identifier.IndexOf(':', separator + 1) < 0
               && !identifier[..separator].Contains('/');
    }
}
