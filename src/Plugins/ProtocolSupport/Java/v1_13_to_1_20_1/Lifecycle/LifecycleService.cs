using Microsoft.Extensions.Logging;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Channels.Extensions;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Services.Lifecycle;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Extensions;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Packets.Clientbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Lifecycle;

public class LifecycleService(ILogger<LifecycleService> logger, IEventService events, IPlayerService players) : AbstractLifecycleService(logger, events, players)
{
    protected override async ValueTask EnableCompressionAsync(ILink link, CancellationToken cancellationToken)
    {
        await link.SendPacketAsync(new SetCompressionPacket { Threshold = 256 }, cancellationToken);
    }

    protected override bool IsSupportedVersion(ProtocolVersion protocolVersion)
    {
        return Plugin.SupportedVersions.Contains(protocolVersion);
    }

    protected override async ValueTask<bool> SendChatMessageAsync(IPlayer player, Component text, CancellationToken cancellationToken)
    {
        if (!await player.IsPlayingAsync(cancellationToken))
            return false;

        var channel = await player.GetChannelAsync(cancellationToken);

        if (player.ProtocolVersion < ProtocolVersion.MINECRAFT_1_19)
            await channel.SendPacketAsync(new ChatMessagePacket { Message = text, Position = 1 }, cancellationToken);
        else
            await channel.SendPacketAsync(new SystemChatMessagePacket { Message = text, Overlay = false }, cancellationToken);

        return true;
    }

    protected override async ValueTask<bool> KickPlayerAsync(IPlayer player, Component reason, CancellationToken cancellationToken)
    {
        var channel = await player.GetChannelAsync(cancellationToken);

        if (await player.IsPlayingAsync(cancellationToken))
        {
            await channel.SendPacketAsync(new JsonDisconnectPacket { Reason = reason }, cancellationToken);
        }
        else if (await player.IsLoggingInAsync(cancellationToken))
        {
            await channel.SendPacketAsync(new JsonDisconnectPacket { Reason = reason }, cancellationToken);
        }
        else
        {
            return false;
        }

        return true;
    }
}
