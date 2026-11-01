using Microsoft.Extensions.Logging;
using Void.Minecraft.Buffers;
using Void.Minecraft.Events;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Channels.Extensions;
using Void.Minecraft.Network.Messages.Binary;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.PacketId;
using Void.Minecraft.Network.Registries.PacketId.Extensions;
using Void.Minecraft.Network.Registries.Transformations;
using Void.Minecraft.Network.Registries.Transformations.Mappings;
using Void.Minecraft.Network.Streams.Packet;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Channels;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Streams.Recyclable;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.Common.Network.Messages.Binary;
using Void.Proxy.Plugins.Common.Network.Registries.Transformations.Mappings;

namespace Void.Proxy.Plugins.Common.Services.Registries;

public abstract class AbstractRegistryService(ILogger<AbstractRegistryService> logger, IPlugin plugin, IPlayerService players, ILinkService links, IEventService events) : IPluginCommonService
{
    [Subscribe(PostOrder.First)]
    public void OnPluginUnloaded(PluginUnloadedEvent @event)
    {
        foreach (var player in players.All)
        {
            // The player's own channel keeps what plugins registered on it while it joins or moves between servers, without a link
            var pendingLink = links.TryGetWeakLink(player, out var weakLink) ? weakLink : null;
            INetworkChannel?[] channels = [player.Context.Channel, player.Link?.PlayerChannel, player.Link?.ServerChannel, pendingLink?.ServerChannel];

            // A channel that carries no Minecraft packets, as one built while no protocol read its handshake does, keeps no registries
            foreach (var channel in channels.OfType<INetworkChannel>().Distinct())
            {
                if (channel.TryGet<IMinecraftPacketMessageStream>(out var stream))
                    stream.Registries.ClearPlugin(@event.Plugin);
            }
        }
    }

    [Subscribe(PostOrder.First)]
    public async ValueTask OnChannelCreated(ChannelCreatedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        await @event.Player.SetPhaseAsync(link: null, @event.Side, Phase.Handshake, @event.Channel, cancellationToken);
    }

    [Subscribe(PostOrder.First + 1)]
    public void OnLinkStopped(LinkStoppedEvent @event)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        var hasActiveReplacement = links.TryGetLink(@event.Player, out var activeLink) && !ReferenceEquals(activeLink, @event.Link);
        var hasPendingReplacement = links.TryGetWeakLink(@event.Player, out var pendingLink) && !ReferenceEquals(pendingLink, @event.Link);

        ClearLinkTransformations(@event.Link.PlayerChannel, @event.Link.ServerChannel, hasActiveReplacement || hasPendingReplacement);
    }

    [Subscribe(PostOrder.First)]
    public void OnPhaseChanged(PhaseChangedEvent @event)
    {
        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        // At handshake phase IPlayer channel is still being built, causing stack overflow here
        if (@event.Phase is Phase.Handshake)
        {
            SetupRegistries(@event.Channel, @event.Side, @event.Player.ProtocolVersion);
            return;
        }

        if (@event.Link is { } link)
        {
            // Clear only affected registries that are no longer valid

            if (@event.Side is Side.Client)
                link.PlayerChannel.MinecraftRegistries.ClearPlugins();

            if (@event.Side is Side.Server)
                link.ServerChannel.MinecraftRegistries.ClearPlugins();
        }
        else
        {
            @event.Channel.MinecraftRegistries.ClearPlugins();
        }
    }

    [Subscribe]
    public async ValueTask OnMessageReceivedWithCustomRegistry(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        var operation = Operation.Read;
        var channel = @event.Direction switch
        {
            Direction.Clientbound => @event.Link.ServerChannel,
            Direction.Serverbound => await @event.Player.GetChannelAsync(cancellationToken),
            _ => throw new InvalidOperationException($"Unknown direction {@event.Direction}")
        };

        var registries = channel.MinecraftRegistries.PacketIdPlugins;

        if (registries.IsEmpty)
            return;

        if (registries.Contains(@event.Message))
            return;

        var transformations = channel.MinecraftRegistries.PacketTransformationsPlugins;

        try
        {
            var packets = @event.Message switch
            {
                IMinecraftBinaryMessage binaryMessage => DecodeBinaryMessage(@event.Link, operation, registries, transformations, binaryMessage),
                IMinecraftPacket minecraftPacket => DecodeMinecraftPacket(@event.Link, @event.Player, operation, registries, transformations, minecraftPacket),
                _ => null
            };

            if (packets is null)
                return;

            foreach (var packet in packets)
                @event.Result = await events.ThrowWithResultAsync(new MessageReceivedEvent(@event.Origin, @event.From, @event.To, @event.Direction, packet, @event.Link, @event.Player), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error decoding or handling {Direction} {Type}", @event.Direction, @event.Message);
        }
    }

    [Subscribe]
    public async ValueTask OnMessageSentWithCustomRegistry(MessageSentEvent @event, CancellationToken cancellationToken)
    {
        if (!@event.Player.IsMinecraft)
            return;

        if (!IsSupportedVersion(@event.Player.ProtocolVersion))
            return;

        var operation = Operation.Write;
        var channel = @event.Direction switch
        {
            Direction.Clientbound => @event.Link.PlayerChannel,
            Direction.Serverbound => @event.Link.ServerChannel,
            _ => throw new InvalidOperationException($"Unknown direction {@event.Direction}")
        };

        var registries = channel.MinecraftRegistries.PacketIdPlugins;

        if (registries.IsEmpty)
            return;

        if (registries.Contains(@event.Message))
            return;

        var transformations = channel.MinecraftRegistries.PacketTransformationsPlugins;

        try
        {
            var packets = @event.Message switch
            {
                IMinecraftBinaryMessage binaryMessage => DecodeBinaryMessage(@event.Link, operation, registries, transformations, binaryMessage),
                IMinecraftPacket minecraftPacket => DecodeMinecraftPacket(@event.Link, @event.Player, operation, registries, transformations, minecraftPacket),
                _ => null
            };

            if (packets is null)
                return;

            foreach (var packet in packets)
                await events.ThrowAsync(new MessageSentEvent(@event.Origin, @event.From, @event.To, @event.Direction, packet, @event.Link, @event.Player), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error decoding or handling {Direction} {Type}", @event.Direction, @event.Message);
        }
    }

    [Subscribe]
    public void OnPluginUnloading(PluginUnloadingEvent @event)
    {
        if (@event.Plugin != plugin)
            return;

        events.UnregisterListeners(this);

        players.ForEach(player =>
        {
            // A player still connecting has no channel to keep the plugin's registries yet, and building one here would build it for the
            // player as it was before a protocol took it
            if (player.Context.Channel is not { } channel || !channel.TryGet<IMinecraftPacketMessageStream>(out _))
                return;

            if (links.TryGetLink(player, out var link))
                link.ServerChannel.DisposeRegistries(plugin);

            channel.DisposeRegistries(plugin);
        });
    }

    protected static IEnumerable<IMinecraftPacket> DecodeBinaryMessage(ILink link, Operation operation, IMinecraftPacketIdPluginsRegistry registries, IMinecraftPacketTransformationsPluginsRegistry transformationsMappings, IMinecraftBinaryMessage binaryMessage)
    {
        if (!link.Player.IsMinecraft)
            yield break;

        var filteredRegistries = operation switch
        {
            Operation.Read => registries.Read,
            Operation.Write => registries.Write,
            _ => []
        };

        var queue = new Queue<IMinecraftPacketIdRegistry>(filteredRegistries);
        while (queue.TryDequeue(out var registry))
        {
            if (!registry.TryCreateDecoder(binaryMessage.Id, out var type, out var decoder))
                continue;

            // Do not pass origin binary message to transformers, so plugins cannot modify it
            var position = binaryMessage.Stream.Position;

            using var stream = RecyclableStream.RecyclableMemoryStreamManager.GetStream();
            binaryMessage.Stream.CopyTo(stream);
            binaryMessage.Stream.Position = position;

            stream.Position = 0;
            var buffer = new MinecraftBuffer(stream);
            var wrapper = new MinecraftBinaryPacketWrapper(new MinecraftBinaryPacket(binaryMessage.Id, stream));

            if (registries.TryGetTransformations(transformationsMappings, type, TransformationType.Upgrade, out var transformations))
            {
                foreach (var transformation in transformations)
                {
                    transformation(wrapper);
                    wrapper.Reset();
                    stream.Position = 0;
                }
            }

            wrapper.WriteProcessedValues(ref buffer);

            stream.SetLength(stream.Position);
            stream.Position = 0;

            var packet = decoder(ref buffer, link.Player.ProtocolVersion);

            yield return packet;
        }
    }

    protected static IEnumerable<IMinecraftPacket> DecodeMinecraftPacket(ILink? link, IPlayer player, Operation operation, IMinecraftPacketIdPluginsRegistry registries, IMinecraftPacketTransformationsPluginsRegistry transformationsMappings, IMinecraftPacket minecraftPacket)
    {
        if (!player.IsMinecraft)
            yield break;

        var playerRegistry = player.Context.Channel?.MinecraftRegistries.PacketIdSystem;
        var serverRegistry = link?.ServerChannel.MinecraftRegistries.PacketIdSystem;

        if (!(playerRegistry?.Write.TryGetPacketId(minecraftPacket, out var id) ?? false) &&
            !(serverRegistry?.Write.TryGetPacketId(minecraftPacket, out id) ?? false) &&
            !(playerRegistry?.Read.TryGetPacketId(minecraftPacket, out id) ?? false) &&
            !(serverRegistry?.Read.TryGetPacketId(minecraftPacket, out id) ?? false))
            yield break;

        var filteredRegistries = operation switch
        {
            Operation.Read => registries.Read,
            Operation.Write => registries.Write,
            _ => []
        };

        var queue = new Queue<IMinecraftPacketIdRegistry>(filteredRegistries);
        while (queue.TryDequeue(out var registry))
        {
            if (!registry.TryCreateDecoder(id, out var type, out var decoder))
                continue;

            using var stream = RecyclableStream.RecyclableMemoryStreamManager.GetStream();
            var buffer = new MinecraftBuffer(stream);
            var wrapper = new MinecraftBinaryPacketWrapper(new MinecraftBinaryPacket(id, stream));

            minecraftPacket.Encode(ref buffer, player.ProtocolVersion);
            stream.Position = 0;

            if (registries.TryGetTransformations(transformationsMappings, type, TransformationType.Upgrade, out var transformations))
            {
                foreach (var transformation in transformations)
                {
                    transformation(wrapper);
                    wrapper.Reset();
                    stream.Position = 0;
                }
            }

            wrapper.WriteProcessedValues(ref buffer);

            stream.SetLength(stream.Position);
            stream.Position = 0;

            var packet = decoder(ref buffer, player.ProtocolVersion);

            stream.Position = 0;
            yield return packet;
        }
    }

    internal static void ClearLinkTransformations(INetworkChannel playerChannel, INetworkChannel serverChannel, bool preservePlayerChannel)
    {
        // The player still plays its protocol without a link, so the proxy's own packets to it, such as a kick as the proxy stops, still
        // need the system transformations of its version. The next link registers them only where they are missing, and the protocol plugin
        // releases them as it unloads.
        if (!preservePlayerChannel)
            ClearTransformations(playerChannel, system: false);

        ClearTransformations(serverChannel, system: true);

        static void ClearTransformations(INetworkChannel channel, bool system)
        {
            if (!channel.TryGet<IMinecraftPacketMessageStream>(out var packetStream))
                return;

            if (system)
                packetStream.Registries.PacketTransformationsSystem.Clear();

            packetStream.Registries.PacketTransformationsPlugins.Clear();
        }
    }

    protected abstract bool IsSupportedVersion(ProtocolVersion version);
    protected abstract void SetupRegistries(INetworkChannel channel, Side side, ProtocolVersion protocolVersion);
}
