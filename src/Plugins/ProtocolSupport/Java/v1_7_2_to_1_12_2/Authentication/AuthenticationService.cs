using Microsoft.Extensions.Logging;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Definitions;
using Void.Minecraft.Network.Messages.Binary;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Messages;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Services.Authentication;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Extensions;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Packets.Clientbound;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Packets.Serverbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_7_2_to_1_12_2.Authentication;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
public class AuthenticationService(ILogger<AuthenticationService> logger, IEventService events, IPlayerService players, IServerService servers, IConsoleService console, IDependencyService dependencies) : AbstractAuthenticationService(events, players, servers, console, dependencies)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
{
    protected override bool IsSupportedVersion(ProtocolVersion protocolVersion)
    {
        return Plugin.SupportedVersions.Contains(protocolVersion);
    }

    protected override async ValueTask<bool> IsPlayerAuthenticatedAsync(IPlayer player, CancellationToken cancellationToken)
    {
        return await player.IsAuthenticatedAsync(cancellationToken);
    }

    protected override async ValueTask<bool> IsPlayerPlayingAsync(IPlayer player, CancellationToken cancellationToken)
    {
        return await player.IsPlayingAsync(cancellationToken);
    }

    protected override async ValueTask<bool> StartPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return false;

        var loginStart = await link.ReceivePacketAsync<LoginStartPacket>(cancellationToken);

        if (IsAlreadyOnline(loginStart.Profile.Username))
            return false;

        link.Player.Profile = loginStart.Profile;
        return true;
    }

    protected override async ValueTask FinishPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        if (link.Player.Profile is not { } profile)
            throw new InvalidOperationException($"Player should be logged in already");

        await link.SendPacketAsync(new LoginSuccessPacket { GameProfile = profile }, cancellationToken);
    }

    protected override async ValueTask HandshakeWithServerAsync(ILink link, IMinecraftServerboundPacket? packet, int nextState, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        await link.SendPacketAsync(packet ?? new HandshakePacket { NextState = nextState, ProtocolVersion = link.Player.ProtocolVersion.Value, ServerAddress = link.Server.Host, ServerPort = (ushort)link.Server.Port }, cancellationToken);
    }

    protected override async ValueTask StartServerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        if (link.Player.Profile is not { } profile)
            throw new InvalidOperationException("Player should be logged in already");

        await link.SendPacketAsync(new LoginStartPacket { Profile = profile }, cancellationToken);
    }

    protected override async ValueTask<bool> FinishServerAuthenticationAsync(ILink link, AuthenticationResult authenticationResult, HandshakeBuildEventResult handshakeBuildEventResult, CancellationToken cancellationToken)
    {
        // Forge handshakes with Plugin Message packets after Login Success, and sends Join Game only once the player has answered them
        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serverPlaying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var relay = RelayPlayerPluginMessagesAsync(link, serverPlaying.Task, relayCancellation.Token, serverCancellation, cancellationToken);

        IMinecraftClientboundPacket? packet;

        try
        {
            // Packets the proxy handles itself, such as Keep Alives it answers, are canceled
            while ((packet = await link.ReceiveCancellablePacketAsync<IMinecraftClientboundPacket>(serverCancellation.Token)) is not (JoinGamePacket or DisconnectPacket))
            {
                // The server reads the player's packets as play ones only from its first packet after Login Success
                serverPlaying.TrySetResult();

                if (packet is not null)
                    await link.SendPacketAsync(packet, cancellationToken);
            }
        }
        finally
        {
            await relayCancellation.CancelAsync();
            await relay;
        }

        if (packet is DisconnectPacket disconnectPacket)
        {
            logger.LogInformation("Player {Player} cannot join {Server}: {Reason}", link.Player, link.Server, disconnectPacket.Reason);

            // As when the server disconnects the player during login, the player leaves with its reason, also because a Forge handshake the
            // player has started can't continue on another server
            await link.Player.KickAsync(disconnectPacket.Reason, cancellationToken);
            return false;
        }

        var joinGamePacket = (JoinGamePacket)packet;

        if (authenticationResult == AuthenticationResult.AlreadyAuthenticated)
        {
            var respawnPacket = RespawnPacket.FromJoinGame(joinGamePacket);
            var temporaryDimension = joinGamePacket.Dimension == 0 ? -1 : 0;

            if (handshakeBuildEventResult.RequiresExtraRespawn)
            {
                await link.SendPacketAsync(joinGamePacket, cancellationToken);
                await link.SendPacketAsync(RespawnPacket.FromJoinGame(joinGamePacket, temporaryDimension), cancellationToken);
            }
            else
            {
                joinGamePacket.Dimension = temporaryDimension;
                await link.SendPacketAsync(joinGamePacket, cancellationToken);
            }

            await link.SendPacketAsync(respawnPacket, cancellationToken);
        }
        else
            await link.SendPacketAsync(joinGamePacket, cancellationToken);

        return true;
    }

    private async Task RelayPlayerPluginMessagesAsync(ILink link, Task serverPlaying, CancellationToken waitCancellationToken, CancellationTokenSource serverCancellation, CancellationToken cancellationToken)
    {
        var pluginMessageId = PacketIdDefinitions.ServerboundPlayPluginMessage.Last(mapping => mapping.ProtocolVersion <= link.Player.ProtocolVersion).Id;

        try
        {
            while (await ReadPlayerMessageAsync() is { } message)
            {
                using (message)
                {
                    // Anything the player sends before the server starts talking, and anything but plugin messages before Join Game, belongs
                    // to the world it is leaving, such as messages of its mods while it switches servers
                    if (!serverPlaying.IsCompleted || message is not IMinecraftBinaryMessage { Id: var id } pluginMessage || id != pluginMessageId)
                    {
                        logger.LogTrace("Dropping {Message} that player {Player} sent before joining {Server}", message, link.Player, link.Server);
                        continue;
                    }

                    if (await events.ThrowWithResultAsync(new MessageReceivedEvent(Side.Client, Side.Client, Side.Proxy, Direction.Serverbound, message, link, link.Player), cancellationToken))
                        continue;

                    await link.SendPacketAsync(Side.Server, pluginMessage, cancellationToken);
                }
            }
        }
        catch
        {
            // Without the player's answers the server never sends Join Game
            await serverCancellation.CancelAsync();
            throw;
        }

        async ValueTask<INetworkMessage?> ReadPlayerMessageAsync()
        {
            try
            {
                return await link.PlayerChannel.ReadMessageAsync(waitCancellationToken, cancellationToken);
            }
            catch (OperationCanceledException) when (waitCancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    protected override async ValueTask<AuthenticationResult> HandleServerPacketAsync(ILink link, IMinecraftClientboundPacket packet, CancellationToken cancellationToken)
    {
        switch (packet)
        {
            case DisconnectPacket disconnectPacket:
                logger.LogInformation("Player {Player} cannot authenticate on {Server}: {Reason}", link.Player, link.Server, disconnectPacket.Reason);

                // Since the player has already completed the login state, it cannot be kicked with a login disconnect packet
                await link.Player.KickAsync(disconnectPacket.Reason, cancellationToken);
                return AuthenticationResult.NotAuthenticatedServer;
            case LoginSuccessPacket:
                return AuthenticationResult.Authenticated;
            case SetCompressionPacket:
                // handled by compression service
                break;
            case LoginPluginRequestPacket loginPluginRequest:
                var loginPluginMessage = new LoginPluginMessageEvent(link.Player, link, loginPluginRequest.Channel, loginPluginRequest.Data);

                if (!await events.ThrowWithResultAsync(loginPluginMessage, cancellationToken))
                    break;

                await link.SendPacketAsync(new LoginPluginResponsePacket { MessageId = loginPluginRequest.MessageId, Data = loginPluginMessage.Response ?? [], Successful = loginPluginMessage.Successful }, cancellationToken);
                break;
            case EncryptionRequestPacket:
                throw new InvalidOperationException("Authentication side is set to Proxy, but server is in online-mode.");
            default:
                throw new InvalidOperationException($"Unexpected {packet} packet received");
        }

        return AuthenticationResult.NoResult;
    }
}
