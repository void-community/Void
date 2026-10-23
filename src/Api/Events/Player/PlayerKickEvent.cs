using Void.Proxy.Api.Players;

namespace Void.Proxy.Api.Events.Player;

/// <summary>
/// Requests that a player be disconnected from the proxy.
/// </summary>
/// <param name="Player">The player to disconnect.</param>
/// <param name="Text">The optional disconnect message; <see langword="null" /> uses the implementation's default message.</param>
public record PlayerKickEvent(IPlayer Player, string? Text = null) : IScopedEventWithResult<bool>
{
    /// <summary>
    /// Gets or sets the disconnect message, which listeners can replace before the player is disconnected.
    /// </summary>
    /// <value>The disconnect message, or <see langword="null" /> to use the implementation's default message.</value>
    public string? Text { get; set; } = Text;

    /// <summary>
    ///     <see langword="true" /> if kick was made; otherwise, <see langword="false" />.
    /// </summary>
    public bool Result { get; set; }
}
