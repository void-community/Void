using Microsoft.Extensions.Logging;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;

namespace Void.Proxy.Plugins.ModsSupport.CrossStitch;

/// <summary>
/// Unwraps the arguments CrossStitch servers wrap for proxies, so players get each one with the server's own argument type and properties, as
/// on a direct connection.
/// </summary>
public class CrossStitchService : IEventListener
{
    public static readonly ArgumentSerializerMapping ModArgumentMapping = new("crossstitch:mod_argument", ProtocolVersion.MINECRAFT_1_19, parserId: -256);

    // Other listeners see the server's own argument types.
    [Subscribe(PostOrder.First)]
    public void OnAvailableCommands(AvailableCommandsEvent @event)
    {
        if (!@event.Player.IsMinecraft)
            return;

        Unwrap(@event.Node, @event.Player.ProtocolVersion);
    }

    /// <summary>Replaces each argument CrossStitch wrapped in <paramref name="root"/> with the argument it wraps.</summary>
    /// <param name="root">The command tree as the server sent it.</param>
    /// <param name="protocolVersion">The protocol version the tree was written for.</param>
    public static void Unwrap(RootCommandNode root, ProtocolVersion protocolVersion)
    {
        Unwrap(root, protocolVersion, new HashSet<CommandNode>(ReferenceEqualityComparer.Instance));
    }

    private static void Unwrap(CommandNode node, ProtocolVersion protocolVersion, HashSet<CommandNode> visited)
    {
        if (!visited.Add(node))
            return;

        if (node is ArgumentCommandNode { Type: UnknownArgumentType argumentType } argument && TryUnwrap(argumentType, protocolVersion) is { } wrappedArgumentType)
            argument.Type = wrappedArgumentType;

        foreach (var child in node.Children)
            Unwrap(child, protocolVersion, visited);
    }

    private static UnknownArgumentType? TryUnwrap(UnknownArgumentType argumentType, ProtocolVersion protocolVersion)
    {
        if (!IsModArgument(argumentType.Mapping, protocolVersion))
            return null;

        try
        {
            var buffer = new BufferSpan(argumentType.Properties.ToArray());
            ArgumentSerializerMapping mapping;

            // The server's own parser ID goes back as read, even -1, which servers write for an argument type they never registered.
            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
            {
                var parserId = buffer.ReadVarInt();
                mapping = new ArgumentSerializerMapping($"unknown:{parserId}", protocolVersion, parserId);
            }
            else
            {
                mapping = new ArgumentSerializerMapping(buffer.ReadString());
            }

            var properties = buffer.ReadByteArray().ToArray();
            return buffer.Remaining is 0 ? new UnknownArgumentType(mapping, properties) : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Properties that don't hold a wrapped argument reach the player as the server sent them.
            return null;
        }
    }

    private static bool IsModArgument(ArgumentSerializerMapping mapping, ProtocolVersion protocolVersion)
    {
        if (protocolVersion < ProtocolVersion.MINECRAFT_1_19)
            return mapping.Identifier == ModArgumentMapping.Identifier;

        return mapping.VersionParserIdMapping.TryGetValue(protocolVersion, out var parserId) && parserId == ModArgumentMapping.VersionParserIdMapping[protocolVersion];
    }
}
