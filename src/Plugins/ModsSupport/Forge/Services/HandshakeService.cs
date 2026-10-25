using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Events;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Definitions;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.ModsSupport.Forge.Packets;

namespace Void.Proxy.Plugins.ModsSupport.Forge.Services;

public class HandshakeService(IPlayerContext context) : IEventListener
{
    private const byte LegacyServerHelloDiscriminator = 0;
    private static readonly byte[] LegacyHandshakeResetData = [0xFE, 0x00];

    private readonly ConcurrentDictionary<IPlayer, PlayerCachedMods> _playerMods = [];
    private readonly LegacyHandshake _legacyHandshake = new();
    private bool _joinedServer;

    // The handshake recorded here is how a modded player joins the next server, which a plugin loaded again has no record of, so the player
    // leaves with why rather than with a switch that fails. First, as the kick needs the packet registries that unloading disposes of.
    [Subscribe(PostOrder.First)]
    public async ValueTask OnPluginUnloading(PluginUnloadingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Plugin.GetType().Assembly != GetType().Assembly)
            return;

        if (!_playerMods.ContainsKey(context.Player) && !_legacyHandshake.IsComplete)
            return;

        try
        {
            await context.Player.KickAsync("Void unloaded support for your mods", cancellationToken);
        }
        catch (StreamClosedException)
        {
            // The player left before the kick
        }
    }

    [Subscribe]
    public void OnPlayerDisconnect(PlayerDisconnectedEvent @event)
    {
        _ = _playerMods.TryRemove(@event.Player, out _);
    }

    [Subscribe]
    public void OnPlayerJoinedServer(PlayerJoinedServerEvent @event)
    {
        _joinedServer = true;
        _legacyHandshake.Complete();
    }

    [Subscribe]
    public async ValueTask OnLegacyServerHello(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        if (@event is not { Direction: Direction.Clientbound, Message: PluginMessagePacket { Channel: LegacyHandshake.Channel } packet } || packet.Data.Span is not [LegacyServerHelloDiscriminator, ..])
            return;

        // A legacy Forge client that has started a handshake ignores the next server's hello until it's reset. 1.7 clients don't mark
        // themselves as Forge, so players get one once their hello shows they can take it, and the proxy answers handshakes for the rest.
        if (!_legacyHandshake.CanReset)
            return;

        context.Logger.LogDebug("Resetting legacy Forge handshake before {Server} starts it", @event.Link.Server);
        await @event.Link.SendPacketAsync(Side.Client, new PluginMessagePacket { Channel = LegacyHandshake.Channel, Data = LegacyHandshakeResetData }, cancellationToken);
    }

    [Subscribe]
    public void OnLegacyPluginMessageWhileSwitching(MessageReceivedEvent @event)
    {
        // Only Minecraft players get plugin messages
        if (@event.Message is not PluginMessagePacket || !_joinedServer || @event.Player.ProtocolVersion >= ProtocolVersion.MINECRAFT_1_13)
            return;

        // Until the next server's Join Game, the player's mods keep sending messages about the world it is leaving, which only the
        // handshake with the next server should get past
        if (@event is { Direction: Direction.Serverbound, Link.IsAlive: false, Message: PluginMessagePacket { Channel: not (LegacyHandshake.Channel or "REGISTER" or "UNREGISTER") } })
            @event.Cancel();
    }

    [Subscribe]
    public async ValueTask OnLegacyHandshakeMessage(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        // Only Minecraft players get plugin messages, and only legacy Forge handshakes on this channel
        if (@event.Message is not PluginMessagePacket packet || @event.Player.ProtocolVersion >= ProtocolVersion.MINECRAFT_1_13)
            return;

        // The handshake the player first joins a server through shows how to answer the handshakes of later servers for a player that can't
        // take a reset
        if (!_legacyHandshake.IsComplete)
        {
            if (@event.Direction is Direction.Clientbound && packet.Channel is LegacyHandshake.Channel)
                _legacyHandshake.RecordServerMessage(packet.Data.Span);
            else if (@event.Direction is Direction.Serverbound && packet.Channel is LegacyHandshake.Channel or "REGISTER")
                _legacyHandshake.RecordPlayerMessage(packet.Channel, packet.Data.Span);

            return;
        }

        if (!_legacyHandshake.AnswersForPlayer || @event.Direction is not Direction.Clientbound || packet.Channel is not LegacyHandshake.Channel)
            return;

        // Such a player ignores every handshake after its first, so the proxy answers the server as the player answered the first one
        @event.Cancel();

        if (!_legacyHandshake.KeepsIds(packet.Data.Span))
        {
            // The player would show the server's blocks and items by the ids of the first server, so it goes back to the previous server
            context.Logger.LogWarning("Player {Player} can't join {Server}, which numbers its blocks and items differently from the first server the player joined", @event.Player, @event.Link.Server);
            @event.Link.ServerChannel.Close();
            return;
        }

        foreach (var answer in _legacyHandshake.AnswersTo(packet.Data.Span))
            await @event.Link.SendPacketAsync(Side.Server, answer, cancellationToken);
    }

    [Subscribe]
    public void OnServerboundHandshakeBuild(HandshakeBuildEvent @event)
    {
        if (!_playerMods.TryGetValue(@event.Player, out var mods))
            return;

        @event.Result = new HandshakeBuildEventResult(mods.HandshakePacket, mods.HandshakePacket.ServerAddress, mods.HandshakePacket.NextState, mods.HandshakePacket.Markers.Contains(ForgeMarker.Fml.Name));
    }

    [Subscribe]
    public async ValueTask OnLoginPluginMessageEvent(LoginPluginMessageEvent @event, CancellationToken cancellationToken)
    {
        const string handshakeChannel = "fml:handshake";

        if (!_playerMods.TryGetValue(@event.Player, out var mods))
            return;

        if (@event is { Channel: LoginWrapperMessage.WrapperChannel })
        {
            var (channel, id, index) = LoginWrapperMessage.Read(@event.Data);

            // Forge and NeoForge register their mod data message without a response, so clients never answer it
            var ignored = channel is handshakeChannel && id is 5;

            if (@event.Player.Phase is not Phase.Login)
            {
                if (ignored)
                    return;

                if (!mods.FmlHandshakeResponses.TryGetValue(id, out var response))
                    throw new InvalidOperationException($"No cached FML handshake response found for player {@event.Player} with request id {id}.");

                // The cached answer repeats the index of the request it answered, while NeoForge matches it to this request by its own
                @event.Response = index is { } requestIndex ? LoginWrapperMessage.WithIndex(response, requestIndex) : response;
                @event.Successful = true;
                @event.Result = true;
                return;
            }

            if (!ignored)
            {
                if (mods is { PendingLoginPluginResponse.Task.IsCompleted: false })
                    throw new InvalidOperationException($"A new login plugin message request in {@event.Channel} to player {@event.Player} received, while it is already pending a response.");

                mods.PendingLoginPluginResponse = new TaskCompletionSource<ModdedLoginPluginResponsePacket>();
            }

            await @event.Link.SendPacketAsync(new ModdedLoginPluginRequestPacket { MessageId = GetLoginPluginRequestIdFor(@event.Player), Channel = @event.Channel, Data = @event.Data }, cancellationToken);

            if (!ignored && mods.PendingLoginPluginResponse is { } pendingLoginPluginResponse)
            {
                // Receive system-defined login plugin message response so it triggers MessageReceivedEvent for modded login plugin message response
                _ = await @event.Link.ReceivePacketAsync<IMinecraftServerboundPacket>(cancellationToken);

                var moddedLoginPluginResponsePacket = await pendingLoginPluginResponse.Task;

                @event.Response = moddedLoginPluginResponsePacket.Data;
                @event.Successful = moddedLoginPluginResponsePacket.Successful;
                @event.Result = true;

                mods.FmlHandshakeResponses[id] = moddedLoginPluginResponsePacket.Data;
            }
        }
    }

    [Subscribe]
    public void OnMessageReceived(MessageReceivedEvent @event)
    {
        if (!@event.Player.IsMinecraft)
            return;

        switch (@event.Message)
        {
            case ModdedHandshakePacket { IsForge: true } handshakePacket:
                _playerMods[@event.Player] = new PlayerCachedMods { HandshakePacket = handshakePacket };
                break;

            case ModdedLoginPluginResponsePacket loginPluginResponsePacket:
                if (_playerMods.TryGetValue(@event.Player, out var mods) && mods is { PendingLoginPluginResponse.Task.IsCompleted: false })
                    mods.PendingLoginPluginResponse.SetResult(loginPluginResponsePacket);
                break;
        }
    }

    [Subscribe(PostOrder.First)]
    public void OnPhaseChanged(PhaseChangedEvent @event)
    {
        switch (@event)
        {
            case { Phase: Phase.Handshake }:
                @event.Player.RegisterPacket<ModdedHandshakePacket>(
                    @event.Channel,
                    @event.Side switch
                    {
                        Side.Client => Operation.Read,
                        Side.Server => Operation.Write,
                        _ => throw new InvalidOperationException($"Invalid side changed phase: {@event.Side}")
                    },
                    PacketIdDefinitions.ServerboundHandshake);
                break;

            // Requests from the server are relayed to the player, whose answers complete them; the server's side of both goes through authentication.
            case { Phase: Phase.Login, Side: Side.Client }:
                @event.Player.RegisterPacket<ModdedLoginPluginResponsePacket>(@event.Channel, Operation.Read, PacketIdDefinitions.ServerboundLoginPluginResponse);
                @event.Player.RegisterPacket<ModdedLoginPluginRequestPacket>(@event.Channel, Operation.Write, PacketIdDefinitions.ClientboundLoginPluginRequest);
                break;
            case { Phase: Phase.Play }:
                @event.Player.RegisterPacket<PluginMessagePacket>(
                    @event.Channel,
                    @event.Side switch
                    {
                        Side.Client => Operation.Read,
                        Side.Server => Operation.Write,
                        _ => throw new InvalidOperationException($"Invalid side changed phase: {@event.Side}")
                    },
                    PacketIdDefinitions.ServerboundPlayPluginMessage);
                @event.Player.RegisterPacket<PluginMessagePacket>(
                    @event.Channel,
                    @event.Side switch
                    {
                        Side.Client => Operation.Write,
                        Side.Server => Operation.Read,
                        _ => throw new InvalidOperationException($"Invalid side changed phase: {@event.Side}")
                    },
                    PacketIdDefinitions.ClientboundPlayPluginMessage);
                break;
        }
    }

    private int GetLoginPluginRequestIdFor(IPlayer player)
    {
        return _playerMods[player].LoginPluginRequestCounter++;
    }

    private class PlayerCachedMods
    {
        public required ModdedHandshakePacket HandshakePacket { get; init; }
        public TaskCompletionSource<ModdedLoginPluginResponsePacket>? PendingLoginPluginResponse { get; set; }
        public int LoginPluginRequestCounter { get; set; }
        public Dictionary<int, byte[]> FmlHandshakeResponses { get; set; } = [];
    }
}
