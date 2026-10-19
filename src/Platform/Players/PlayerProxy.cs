using System.Net.Sockets;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;

namespace Void.Proxy.Players;

/// <summary>
/// Acts as a proxy for an IPlayer instance, providing access to its properties and methods while allowing for
/// replacement. This is used to allow for the replacement of player implementations. Example: SimplePlayer => MinecraftPlayer.
/// </summary>
/// <param name="player">The initial player instance that the proxy will represent and manage.</param>
public class PlayerProxy(IPlayer player) : IPlayer
{
    public string Name => ToString() ?? throw new InvalidOperationException("Player has no name");
    public TcpClient Client => Source.Client;
    public string RemoteEndPoint => Source.RemoteEndPoint;
    public DateTimeOffset ConnectedAt => Source.ConnectedAt;
    public IPlayerContext Context => Source.Context;
    public IPlayer Source { get; private set; } = player is not PlayerProxy ? player : throw new InvalidOperationException($"PlayerProxy cannot be set as {nameof(Source)}");

    public void Replace(IPlayer player)
    {
        if (Client != player.Client)
            throw new InvalidOperationException($"Player {Source} and {player} have different TCP clients");

        Source = player;
    }

    public override string? ToString()
    {
        return Source.ToString();
    }

    public bool Equals(IPlayer? other)
    {
        return ((IPlayer)this).GetStableHashCode() == other?.GetStableHashCode();
    }

    public async ValueTask DisposeAsync()
    {
        await Source.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Source.Dispose();
        GC.SuppressFinalize(this);
    }
}
