using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Minecraft.Profiles;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;

namespace Void.Minecraft.Players;

/// <summary>
/// Represents a connected Java Edition Minecraft player.
/// </summary>
/// <param name="client">The accepted TCP client owned by the player context.</param>
/// <param name="context">The player-scoped services and network context.</param>
/// <param name="remoteEndPoint">The preformatted remote endpoint used before a profile name is known.</param>
/// <param name="connectedAt">The timestamp at which the connection was accepted.</param>
/// <param name="protocolVersion">The Java Edition protocol version detected for the connection.</param>
public class MinecraftPlayer(TcpClient client, IPlayerContext context, string remoteEndPoint, DateTimeOffset connectedAt, ProtocolVersion protocolVersion) : IPlayer
{
    /// <summary>
    /// Gets the player's display name.
    /// </summary>
    /// <remarks>
    /// This returns <see cref="ToString"/>, which prefers the authenticated
    /// profile username when one is available and otherwise falls back to the
    /// remote endpoint text.
    /// </remarks>
    public string Name => ToString();
    /// <summary>
    /// Gets the TCP client for the player connection.
    /// </summary>
    public TcpClient Client { get; } = client;

    /// <summary>
    /// Gets the context that owns player-scoped services and network resources.
    /// </summary>
    public IPlayerContext Context { get; } = context;

    /// <summary>
    /// Gets the formatted remote endpoint captured when the player connected.
    /// </summary>
    public string RemoteEndPoint { get; } = remoteEndPoint;

    /// <summary>
    /// Gets the timestamp at which the player connected.
    /// </summary>
    public DateTimeOffset ConnectedAt { get; } = connectedAt;

    /// <summary>
    /// Gets the detected Java Edition protocol version.
    /// </summary>
    public ProtocolVersion ProtocolVersion { get; } = protocolVersion;

    /// <summary>
    /// Gets or sets the identified chat key supplied by protocol versions 1.19 through 1.19.2.
    /// </summary>
    /// <value>The identified key, or <see langword="null" /> when no key was supplied or the protocol does not use one.</value>
    public IdentifiedKey? IdentifiedKey { get; set; } // Only 1.19 - 1.19.2

    /// <summary>
    /// Gets or sets the player's game profile after it becomes available.
    /// </summary>
    /// <value>The game profile, or <see langword="null" /> before identification completes.</value>
    public GameProfile? Profile { get; set; }

    /// <summary>
    /// Gets or sets the current protocol phase of the player connection.
    /// </summary>
    public Phase Phase { get; set; }

    /// <summary>
    /// Returns the authenticated profile name when available, otherwise the captured remote endpoint.
    /// </summary>
    /// <returns>The profile username or remote endpoint text.</returns>
    public override string ToString()
    {
        return Profile?.Username ?? RemoteEndPoint;
    }

    /// <summary>
    /// Sends a message to the player's chat, with the legacy <c>&amp;</c> formatting codes it contains.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token used to cancel sending.</param>
    /// <returns>A task that completes when chat-send event handlers have processed the message.</returns>
    public async ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        await this.SendChatMessageAsync(message, cancellationToken);
    }

    /// <summary>
    /// Determines whether another player has the same stable player hash code.
    /// </summary>
    /// <param name="other">The player to compare, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when both players have the same stable hash code; otherwise, <see langword="false" />.</returns>
    public bool Equals(IPlayer? other)
    {
        return ((IPlayer)this).GetStableHashCode() == other?.GetStableHashCode();
    }

    /// <summary>
    /// Asynchronously disposes the player context and suppresses finalization for this instance.
    /// </summary>
    /// <returns>A task that completes when context disposal finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the player context and suppresses finalization for this instance.
    /// </summary>
    public void Dispose()
    {
        Context.Dispose();
        GC.SuppressFinalize(this);
    }
}
