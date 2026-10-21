using System.Collections.Generic;
using System.Linq;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Players;

namespace Void.Minecraft.Events.Chat;

/// <summary>
/// Signals that a player's command tree was decoded and is available for inspection or augmentation.
/// </summary>
/// <param name="Link">The link on which the command tree was received.</param>
/// <param name="Player">The player receiving the commands.</param>
/// <param name="Node">The mutable root command node decoded from the server packet.</param>
public record AvailableCommandsEvent(ILink Link, IPlayer Player, RootCommandNode Node) : IScopedEvent
{
    /// <summary>
    /// Gets the commands of <see cref="Node"/> with arguments of types the proxy doesn't know, such as ones mods register. Their arguments are
    /// <see cref="UnknownArgumentType"/>, which reach the player as the server sent them, while the commands themselves can be changed, added
    /// to, or removed like any other.
    /// </summary>
    public IReadOnlyList<CommandNode> CommandsWithUnknownArguments => [.. Node.Children.Where(command => HasUnknownArgument(command, new HashSet<CommandNode>(ReferenceEqualityComparer.Instance)))];

    private static bool HasUnknownArgument(CommandNode node, HashSet<CommandNode> visited)
    {
        if (!visited.Add(node))
            return false;

        return node is ArgumentCommandNode { Type: UnknownArgumentType } || node.Children.Any(child => HasUnknownArgument(child, visited));
    }
}
