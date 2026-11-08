using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Components.Text;
using Void.Minecraft.Events;
using Void.Minecraft.Events.Chat;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Channels.Extensions;
using Void.Minecraft.Network.Messages;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.PacketId.Extensions;
using Void.Minecraft.Network.Registries.PacketId.Mappings;
using Void.Minecraft.Network.Registries.Transformations.Extensions;
using Void.Minecraft.Network.Registries.Transformations.Mappings;
using Void.Minecraft.Profiles;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Extensions;

namespace Void.Minecraft.Players.Extensions;

/// <summary>
/// Provides Minecraft-specific state, messaging, and registry operations for proxy players.
/// </summary>
public static class PlayerExtensions
{
    extension(IPlayer player)
    {
        private MinecraftPlayer AsMinecraft => player.TryGetMinecraftPlayer(out var minecraftPlayer) ? minecraftPlayer : throw new InvalidOperationException($"Player is not a {nameof(MinecraftPlayer)}.");

        /// <summary>
        /// Gets whether the player is represented by <see cref="MinecraftPlayer" />.
        /// </summary>
        public bool IsMinecraft => player is MinecraftPlayer;

        /// <summary>
        /// Gets the player's detected Minecraft protocol version.
        /// </summary>
        /// <exception cref="InvalidOperationException">The player is not a <see cref="MinecraftPlayer" />.</exception>
        public ProtocolVersion ProtocolVersion => player.AsMinecraft.ProtocolVersion;

        /// <summary>
        /// Gets or sets the player's current Minecraft protocol phase.
        /// </summary>
        /// <exception cref="InvalidOperationException">The player is not a <see cref="MinecraftPlayer" />.</exception>
        public Phase Phase { get => player.AsMinecraft.Phase; set => player.AsMinecraft.Phase = value; }

        /// <summary>
        /// Gets or sets the player's optional game profile.
        /// </summary>
        /// <exception cref="InvalidOperationException">The player is not a <see cref="MinecraftPlayer" />.</exception>
        public GameProfile? Profile { get => player.AsMinecraft.Profile; set => player.AsMinecraft.Profile = value; }

        /// <summary>
        /// Gets or sets the player's optional identified chat key.
        /// </summary>
        /// <exception cref="InvalidOperationException">The player is not a <see cref="MinecraftPlayer" />.</exception>
        public IdentifiedKey? IdentifiedKey { get => player.AsMinecraft.IdentifiedKey; set => player.AsMinecraft.IdentifiedKey = value; }

        /// <summary>
        /// Gets a category logger named with the runtime player type and current player text.
        /// </summary>
        /// <remarks>Each access asks the scoped <see cref="ILoggerFactory" /> to create a logger for the computed category.</remarks>
        public ILogger Logger
        {
            get
            {
                var contextBuilder = new StringBuilder();
                contextBuilder.Append(player.GetType().Name);
                contextBuilder.Append(" <");
                contextBuilder.Append(player);
                contextBuilder.Append('>');

                var loggerFactory = player.Context.Services.GetRequiredService<ILoggerFactory>();
                return loggerFactory.CreateLogger(contextBuilder.ToString());
            }
        }

        /// <summary>
        /// Publishes a proxy-originated request to send a Minecraft chat component for the player.
        /// </summary>
        /// <param name="message">The chat component to send.</param>
        /// <param name="cancellationToken">A token used to cancel event processing.</param>
        /// <returns>The result left by chat-send event handlers.</returns>
        /// <exception cref="InvalidOperationException">The player is not a <see cref="MinecraftPlayer" />.</exception>
        public async ValueTask<ChatSendResult> SendChatMessageAsync(Component message, CancellationToken cancellationToken = default)
        {
            var events = player.Context.Services.GetRequiredService<IEventService>();
            return await events.ThrowWithResultAsync(new ChatMessageSendEvent(player.AsMinecraft, message, Side.Proxy), cancellationToken);
        }

        /// <summary>
        /// Requests that the player be disconnected with an optional Minecraft text component.
        /// </summary>
        /// <remarks>Minecraft players receive a <see cref="MinecraftPlayerKickEvent" />; other player implementations receive the component serialized as legacy text.</remarks>
        /// <param name="reason">The optional disconnect reason.</param>
        /// <param name="cancellationToken">A token used to cancel kick processing.</param>
        /// <returns>A task that completes when the player service processes the kick request.</returns>
        public async ValueTask KickAsync(Component? reason = null, CancellationToken cancellationToken = default)
        {
            var players = player.Context.Services.GetRequiredService<IPlayerService>();

            if (player.TryGetMinecraftPlayer(out var minecraftPlayer))
                await players.KickPlayerAsync(player, new MinecraftPlayerKickEvent(player, reason), cancellationToken);
            else
                await players.KickPlayerAsync(player, reason?.SerializeLegacy(), cancellationToken);
        }

        /// <summary>
        /// Sends a packet through the channel selected by its clientbound or serverbound marker interface.
        /// </summary>
        /// <typeparam name="T">The Minecraft message type.</typeparam>
        /// <param name="packet">The packet to send.</param>
        /// <param name="cancellationToken">A token used to cancel channel acquisition or sending.</param>
        /// <returns>A task that completes when the packet has been sent.</returns>
        /// <exception cref="InvalidOperationException">The packet has no usable destination channel, including a serverbound packet sent while the player has no active link.</exception>
        public async ValueTask SendPacketAsync<T>(T packet, CancellationToken cancellationToken = default) where T : class, IMinecraftMessage
        {
            var channel = packet switch
            {
                IMinecraftClientboundPacket => await player.GetChannelAsync(cancellationToken),
                IMinecraftServerboundPacket when player.Link is { } link => link.ServerChannel,
                _ => null
            };

            if (channel is null)
                throw new InvalidOperationException("Player is not linked to any server.");

            await channel.SendPacketAsync(packet, cancellationToken);
        }

        /// <summary>
        /// Registers packet identifier mappings for both read and write operations, inferring direction from the packet marker interface.
        /// </summary>
        /// <typeparam name="T">The packet type to register.</typeparam>
        /// <param name="mappings">The protocol-version-specific identifier mappings.</param>
        /// <exception cref="InvalidOperationException">The packet direction cannot be inferred or the required channel is unavailable.</exception>
        public void RegisterPacket<T>(params MinecraftPacketIdMapping[] mappings) where T : IMinecraftPacket
        {
            player.RegisterPacket<T>(Operation.Any, mappings);
        }

        /// <summary>
        /// Registers packet identifier mappings for selected operations, inferring direction from the packet marker interface.
        /// </summary>
        /// <typeparam name="T">The packet type to register.</typeparam>
        /// <param name="operation">The read, write, or combined operations to register.</param>
        /// <param name="mappings">The protocol-version-specific identifier mappings.</param>
        /// <exception cref="InvalidOperationException">The packet direction cannot be inferred or the required channel is unavailable.</exception>
        public void RegisterPacket<T>(Operation operation, params MinecraftPacketIdMapping[] mappings) where T : IMinecraftPacket
        {
            if (typeof(T).IsAssignableTo(typeof(IMinecraftClientboundPacket)))
            {
                player.RegisterPacket<T>(Direction.Clientbound, operation, mappings);
                return;
            }

            if (typeof(T).IsAssignableTo(typeof(IMinecraftServerboundPacket)))
            {
                player.RegisterPacket<T>(Direction.Serverbound, operation, mappings);
                return;
            }

            throw new InvalidOperationException($"Packet {typeof(T).Name} is neither Clientbound nor Serverbound. Specify the direction with {nameof(RegisterPacket)}<{typeof(T).Name}>(Direction, ...).");
        }

        /// <summary>
        /// Registers packet identifier mappings for both read and write operations in an explicit direction.
        /// </summary>
        /// <typeparam name="T">The packet type to register.</typeparam>
        /// <param name="direction">The protocol direction of the packet.</param>
        /// <param name="mappings">The protocol-version-specific identifier mappings.</param>
        /// <exception cref="InvalidOperationException">A channel required for the requested registration is unavailable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A linked player is supplied a direction other than clientbound or serverbound.</exception>
        public void RegisterPacket<T>(Direction direction, params MinecraftPacketIdMapping[] mappings) where T : IMinecraftPacket
        {
            player.RegisterPacket<T>(direction, Operation.Any, mappings);
        }

        /// <summary>
        /// Registers packet identifier mappings for explicit protocol directions and channel operations.
        /// </summary>
        /// <typeparam name="T">The packet type to register.</typeparam>
        /// <param name="direction">The protocol direction of the packet.</param>
        /// <param name="operation">The read, write, or combined operations to register.</param>
        /// <param name="mappings">The protocol-version-specific identifier mappings.</param>
        /// <remarks>With an active or weakly tracked link, read mappings are applied to the source channel and write mappings to the destination channel. Without a link, only operations supported by the player channel can be registered.</remarks>
        /// <exception cref="InvalidOperationException">A channel required for the requested registration is unavailable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A linked player is supplied a direction other than clientbound or serverbound.</exception>
        public void RegisterPacket<T>(Direction direction, Operation operation, params MinecraftPacketIdMapping[] mappings) where T : IMinecraftPacket
        {
            if (FindPlugin<T>(player) is null)
                return;

            if (player.WeakLink is { } link)
            {
                var (fromChannel, toChannel) = direction switch
                {
                    Direction.Clientbound => (link.ServerChannel, link.PlayerChannel),
                    Direction.Serverbound => (link.PlayerChannel, link.ServerChannel),
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
                };

                if (operation.HasFlag(Operation.Read))
                    player.RegisterPacket<T>(fromChannel, Operation.Read, mappings);

                if (operation.HasFlag(Operation.Write))
                    player.RegisterPacket<T>(toChannel, Operation.Write, mappings);
            }
            else if (direction is Direction.Serverbound) // Fallback
            {
                if (player.Context.Channel is null)
                    throw new InvalidOperationException($"Cannot register {nameof(Direction.Serverbound)} {typeof(T)} packet without a Player channel.");

                if (operation.HasFlag(Operation.Write))
                    throw new InvalidOperationException($"Cannot register {nameof(Direction.Serverbound)} {typeof(T)} packet for {Operation.Write} operation without a Server channel.");

                if (operation.HasFlag(Operation.Read))
                    player.RegisterPacket<T>(player.Context.Channel, Operation.Read, mappings);
            }
            else if (direction is Direction.Clientbound) // Fallback
            {
                if (player.Context.Channel is null)
                    throw new InvalidOperationException($"Cannot register {nameof(Direction.Clientbound)} {typeof(T)} packet without a Player channel.");

                if (operation.HasFlag(Operation.Read))
                    throw new InvalidOperationException($"Cannot register {nameof(Direction.Clientbound)} {typeof(T)} packet for {Operation.Read} operation without a Server channel.");

                if (operation.HasFlag(Operation.Write))
                    player.RegisterPacket<T>(player.Context.Channel, Operation.Write, mappings);
            }
            else
            {
                throw new InvalidOperationException($"Cannot register {nameof(Direction.Clientbound)} {typeof(T)} packet without a Server channel.");
            }
        }

        /// <summary>
        /// Registers <typeparamref name="T"/> as the packet type for the specified channel and operation.
        /// </summary>
        /// <typeparam name="T">The packet type to register.</typeparam>
        /// <param name="channel">The network channel whose packet id registry will receive the mapping.</param>
        /// <param name="operation">The packet operation to register the mapping for.</param>
        /// <param name="mappings">The protocol-version-specific packet id mappings to associate with the packet type.</param>
        /// <remarks>
        /// The packet is registered against the plugin returned by <c>GetPluginFromType</c> on <see cref="IPluginService"/>
        /// and uses the player's current Minecraft protocol version when adding the mapping.
        /// </remarks>
        public void RegisterPacket<T>(INetworkChannel channel, Operation operation, params MinecraftPacketIdMapping[] mappings) where T : IMinecraftPacket
        {
            if (FindPlugin<T>(player) is not { } plugin)
                return;

            channel.MinecraftRegistries.PacketIdPlugins.Get(operation, plugin).RegisterPacket<T>(player.AsMinecraft.ProtocolVersion, mappings);
        }

        /// <summary>
        /// Registers packet transformations on both channels of the player's active link.
        /// </summary>
        /// <typeparam name="T">The packet type whose transformations are registered.</typeparam>
        /// <param name="mappings">The protocol-version-specific transformation mappings.</param>
        /// <exception cref="InvalidOperationException">The player has no established link.</exception>
        [Obsolete($"Use {nameof(RegisterTransformations)}<T>({nameof(INetworkChannel)}, {nameof(MinecraftPacketTransformationMapping)}[]) instead.")]
        public void RegisterTransformations<T>(params MinecraftPacketTransformationMapping[] mappings) where T : IMinecraftPacket
        {
            if (FindPlugin<T>(player) is not { } plugin)
                return;

            var link = player.Link ?? throw new InvalidOperationException("Cannot register packet transformations without an established link.");

            link.PlayerChannel.MinecraftRegistries.PacketTransformationsPlugins.Get(plugin).RegisterTransformations<T>(player.AsMinecraft.ProtocolVersion, mappings);
            link.ServerChannel.MinecraftRegistries.PacketTransformationsPlugins.Get(plugin).RegisterTransformations<T>(player.AsMinecraft.ProtocolVersion, mappings);
        }

        /// <summary>
        /// Registers packet transformations in a plugin-specific registry on one channel.
        /// </summary>
        /// <typeparam name="T">The packet type whose transformations are registered.</typeparam>
        /// <param name="channel">The channel whose transformation registry is updated.</param>
        /// <param name="mappings">The protocol-version-specific transformation mappings.</param>
        public void RegisterTransformations<T>(INetworkChannel channel, params MinecraftPacketTransformationMapping[] mappings) where T : IMinecraftPacket
        {
            if (FindPlugin<T>(player) is not { } plugin)
                return;

            channel.MinecraftRegistries.PacketTransformationsPlugins.Get(plugin).RegisterTransformations<T>(player.AsMinecraft.ProtocolVersion, mappings);
        }

        internal bool TryGetMinecraftPlayer([MaybeNullWhen(false)] out MinecraftPlayer minecraftPlayer)
        {
            minecraftPlayer = player as MinecraftPlayer;
            return minecraftPlayer is not null;
        }
    }

    // A listener that an event invoked as its plugin unloaded registers the plugin's packets once the plugin is gone, which then go nowhere, as
    // the plugin's registries went with it. A type of no plugin is still told of.
    private static IPlugin? FindPlugin<T>(IPlayer player)
    {
        var plugins = player.Context.Services.GetRequiredService<IPluginService>();

        if (plugins.TryGetPluginFromType(typeof(T), out var plugin))
            return plugin;

        return AssemblyLoadContext.GetLoadContext(typeof(T).Assembly) is { IsCollectible: true } ? null : plugins.GetPluginFromType<T>();
    }
}
