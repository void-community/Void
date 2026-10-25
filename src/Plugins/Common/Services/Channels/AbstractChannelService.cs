using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Void.Minecraft.Network;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Channels;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Players;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.Common.Network.Channels;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Network.Streams.Network;
using Void.Proxy.Plugins.Common.Network.Streams.Packet;

namespace Void.Proxy.Plugins.Common.Services.Channels;

public abstract class AbstractChannelService : IPluginCommonService
{
    private static readonly TimeSpan AcceptingPollInterval = TimeSpan.FromMilliseconds(10);

    // The plugin's services register their listeners one at a time as it loads, so a player it took before it loaded would meet only some
    private volatile bool _accepting;
    private int _searches;

    [Subscribe]
    public void OnPluginLoaded(PluginLoadedEvent @event)
    {
        if (@event.Plugin.GetType().Assembly == GetType().Assembly)
            _accepting = true;
    }

    // A plugin that unloads takes no new players, which are forwarded as players of a version no plugin supports, while those it took
    // finish with it. First, before its lifecycle waits for them, which it can only do for players it has already taken, so it finishes
    // taking those it was taking meanwhile.
    [Subscribe(PostOrder.First)]
    public async ValueTask OnPluginUnloading(PluginUnloadingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Plugin.GetType().Assembly != GetType().Assembly)
            return;

        _accepting = false;

        // A full fence, so that a search that still found it accepting is counted by then
        Interlocked.MemoryBarrier();

        while (Volatile.Read(ref _searches) > 0)
            await Task.Delay(AcceptingPollInterval, cancellationToken);
    }

    [Subscribe]
    public async ValueTask OnSearchChannelBuilder(SearchChannelBuilderEvent @event, CancellationToken cancellationToken)
    {
        // Counted before it reads whether it accepts, so the plugin unloading waits for a player it found itself accepting
        Interlocked.Increment(ref _searches);

        try
        {
            if (!_accepting || !IsSupportedHandshake(@event.Buffer, out var protocolVersion))
                return;

            // This is definitely a Minecraft connection
            var player = await @event.Player.UpgradeToMinecraftAsync(protocolVersion, cancellationToken);

            @event.Result = ChannelBuilderAsync;
        }
        finally
        {
            Interlocked.Decrement(ref _searches);
        }
    }

    protected abstract bool IsSupportedHandshake(Memory<byte> memory, [MaybeNullWhen(false)] out ProtocolVersion protocolVersion);

    // Static, as a player keeps the builder that read its handshake, which would otherwise keep the protocol plugin whose service built it alive
    private static async ValueTask<INetworkChannel> ChannelBuilderAsync(IPlayer player, Side side, NetworkStream stream, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = new SimpleMinecraftChannel(new SimpleNetworkStream(stream));
        channel.Add<MinecraftPacketMessageStream>();

        return channel;
    }
}
