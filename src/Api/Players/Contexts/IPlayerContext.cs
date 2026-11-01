using Microsoft.Extensions.Logging;
using Void.Proxy.Api.Network.Channels;

namespace Void.Proxy.Api.Players.Contexts;

/// <summary>
/// Owns the player-scoped services, logging, and network resources associated with a connection.
/// </summary>
public interface IPlayerContext : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the current player instance.
    /// </summary>
    public IPlayer Player { get; }

    /// <summary>
    /// Gets the logger instance for logging player-specific events and information.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the service provider that provides access to player scoped services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets or sets the network channel used for communication.
    /// </summary>
    public INetworkChannel? Channel { get; set; }

    /// <summary>
    /// Gets or sets the channel builder that read the handshake of the player, and builds the channels of the player and of the servers
    /// it joins, for as long as the player stays connected.
    /// </summary>
    public IChannelBuilderService? ChannelBuilder { get; set; }

    /// <summary>
    /// Gets the current state of the player context.
    /// </summary>
    public bool IsDisposed { get; }
}
