using Microsoft.Extensions.Logging;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;

namespace Void.Proxy.Plugins.Common.Players.Contexts;

internal record PlayerContext(Func<IPlayer, IServiceProvider> GetServices) : IPlayerContext
{
    // Setter is allowed to upgrade the player into different implementations when required.
    public required IPlayer Player { get; internal set; }
    public ILogger Logger => Player.Logger;
    public INetworkChannel? Channel { get; set; }
    public IChannelBuilderService? ChannelBuilder { get; set; }
    public bool IsDisposed { get; private set; }
    public IServiceProvider Services => GetServices(Player);

    public void Dispose()
    {
        IsDisposed = true;
        ChannelBuilder = null;
        Channel?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        IsDisposed = true;
        ChannelBuilder = null;

        if (Channel is not null)
            await Channel.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
