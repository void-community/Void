using System.Net.Sockets;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;

namespace Void.Proxy.Plugins.Common.Players;

public class SimplePlayer : IPlayer
{
    public string Name => ToString();
    public TcpClient Client { get; }
    public IPlayerContext Context { get; }
    public string RemoteEndPoint { get; }
    public DateTimeOffset ConnectedAt { get; }

    public SimplePlayer(TcpClient client, Func<IPlayer, IPlayerContext> contextBuilder)
    {
        Client = client;
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown?";

        ConnectedAt = DateTimeOffset.Now;
        Context = contextBuilder(this);
    }

    public override string ToString()
    {
        return RemoteEndPoint;
    }

    public ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        // A player whose game isn't known yet has nowhere to show a message.
        return ValueTask.CompletedTask;
    }

    public bool Equals(IPlayer? other)
    {
        return ((IPlayer)this).GetStableHashCode() == other?.GetStableHashCode();
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        Context.Dispose();
        GC.SuppressFinalize(this);
    }
}
