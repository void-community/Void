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
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.ModsSupport.Forge.Packets;

namespace Void.Proxy.Plugins.ModsSupport.Forge.Services;

public class HandshakeService(IPlayerContext context) : IEventListener
{
    private const string LegacyHandshakeChannel = "FML|HS";
    private const byte LegacyServerHelloDiscriminator = 0;
    private static readonly byte[] LegacyHandshakeResetData = [0xFE, 0x00];

    private readonly ConcurrentDictionary<IPlayer, PlayerCachedMods> _playerMods = [];
    private bool _joinedServer;

    [Subscribe]
    public void OnPlayerDisconnect(PlayerDisconnectedEvent @event)
    {
        _ = _playerMods.TryRemove(@event.Player, out _);
    }

    [Subscribe]
    public void OnPlayerJoinedServer(PlayerJoinedServerEvent @event)
    {
        _joinedServer = true;
    }

    [Subscribe]
    public async ValueTask OnLegacyServerHello(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        if (@event is not { Direction: Direction.Clientbound, Message: PluginMessagePacket { Channel: LegacyHandshakeChannel } packet } || packet.Data.Span is not [LegacyServerHelloDiscriminator, ..])
            return;

        // A legacy Forge client that has finished a handshake ignores the next server's hello until it's reset. 1.7 clients don't mark
        // themselves as Forge, and vanilla clients ignore the reset, so every player that has joined a server gets one.
        if (!_joinedServer)
            return;

        context.Logger.LogDebug("Resetting legacy Forge handshake before {Server} starts it", @event.Link.Server);
        await @event.Link.SendPacketAsync(Side.Client, new PluginMessagePacket { Channel = LegacyHandshakeChannel, Data = LegacyHandshakeResetData }, cancellationToken);
    }

    [Subscribe]
    public void OnLegacyPluginMessageWhileSwitching(MessageReceivedEvent @event)
    {
        if (!_joinedServer || @event.Player.ProtocolVersion >= ProtocolVersion.MINECRAFT_1_13)
            return;

        // Until the next server's Join Game, the player's mods keep sending messages about the world it is leaving, which only the
        // handshake with the next server should get past
        if (@event is { Direction: Direction.Serverbound, Link.IsAlive: false, Message: PluginMessagePacket { Channel: not (LegacyHandshakeChannel or "REGISTER" or "UNREGISTER") } })
            @event.Cancel();
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
