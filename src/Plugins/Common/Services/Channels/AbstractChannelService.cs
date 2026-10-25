using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Void.Minecraft.Network;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Channels;
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
    [Subscribe]
    public async ValueTask OnSearchChannelBuilder(SearchChannelBuilderEvent @event, CancellationToken cancellationToken)
    {
        if (!IsSupportedHandshake(@event.Buffer, out var protocolVersion))
            return;
        
        // This is definitely a Minecraft connection
        var player = await @event.Player.UpgradeToMinecraftAsync(protocolVersion, cancellationToken);

        @event.Result = ChannelBuilderAsync;
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
