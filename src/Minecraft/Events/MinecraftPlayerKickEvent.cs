using Void.Minecraft.Components.Text;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Players;

namespace Void.Minecraft.Events;

/// <summary>
/// Represents a player kick event whose reason is supplied as a Minecraft text <see cref="Component"/>.
/// </summary>
/// <param name="Player">The player being kicked.</param>
/// <param name="Reason">
/// The optional kick reason. When provided, it is serialized to legacy text and passed to
/// <see cref="PlayerKickEvent"/>.
/// </param>
public record MinecraftPlayerKickEvent(IPlayer Player, Component? Reason = null) : PlayerKickEvent(Player, Reason?.SerializeLegacy())
{
    /// <summary>
    /// Gets or sets the kick reason that Minecraft players see, which listeners can replace before the player is disconnected.
    /// </summary>
    public Component? Reason { get; set; } = Reason;
}
