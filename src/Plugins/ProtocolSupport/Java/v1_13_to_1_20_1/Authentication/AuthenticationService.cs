using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Extensions;
using Void.Proxy.Plugins.Common.Network.Bundles;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Services.Authentication;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Extensions;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Packets.Clientbound;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Packets.Serverbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_13_to_1_20_1.Authentication;

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

    protected override async ValueTask FinishPlayingAsync(ILink link, CancellationToken cancellationToken)
    {
        // IPlayer might be in the middle of a bundle when the client is not handling packets until the closing bundle is received
        var bundles = link.Player.Context.Services.GetRequiredService<IBundleService>();

        if (bundles.IsActivated)
            await link.SendPacketAsync<BundleDelimiterPacket>(cancellationToken);
    }

    protected override async ValueTask<bool> StartPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return false;

        var loginStart = await link.ReceivePacketAsync<LoginStartPacket>(cancellationToken);

        if (IsAlreadyOnline(loginStart.Profile.Username))
            return false;

        link.Player.Profile = loginStart.Profile;
        link.Player.IdentifiedKey = loginStart.Key;
        return true;
    }

    protected override async ValueTask FinishPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        if (link.Player.Profile is not { } profile)
            throw new InvalidOperationException("Player should be logged in already");

        await link.SendPacketAsync(new LoginSuccessPacket
        {
            GameProfile = profile
        }, cancellationToken);
    }

    protected override async ValueTask HandshakeWithServerAsync(ILink link, IMinecraftServerboundPacket? packet, int nextState, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        await link.SendPacketAsync(packet ?? new HandshakePacket
        {
            NextState = nextState,
            ProtocolVersion = link.Player.ProtocolVersion.Value,
            ServerAddress = link.Server.Host,
            ServerPort = (ushort)link.Server.Port
        }, cancellationToken);
    }

    protected override async ValueTask StartServerLoginAsync(ILink link, CancellationToken cancellationToken)
    {
        if (!link.Player.IsMinecraft)
            return;

        if (link.Player.Profile is not { } profile)
            throw new InvalidOperationException("Player should be logged in already");

        await link.SendPacketAsync(new LoginStartPacket
        {
            Profile = profile,
            Key = link.Player.IdentifiedKey
        }, cancellationToken);
    }

    protected override async ValueTask<bool> FinishServerAuthenticationAsync(ILink link, AuthenticationResult authenticationResult, HandshakeBuildEventResult handshakeBuildEventResult, CancellationToken cancellationToken)
    {
        // Forge sends Plugin Message packets after Login Success but before Join Game
        IMinecraftClientboundPacket packet;
        while ((packet = await link.ReceivePacketAsync<IMinecraftClientboundPacket>(cancellationToken)) is not JoinGamePacket)
            await link.SendPacketAsync(packet, cancellationToken);

        var joinGamePacket = (JoinGamePacket)packet;

        if (authenticationResult == AuthenticationResult.AlreadyAuthenticated)
        {
            var respawnPacket = RespawnPacket.FromJoinGame(joinGamePacket);

            if (link.Player.ProtocolVersion < ProtocolVersion.MINECRAFT_1_16)
                joinGamePacket.Dimension = joinGamePacket.Dimension == 0 ? -1 : 0;

            await link.SendPacketAsync(joinGamePacket, cancellationToken);
            await link.SendPacketAsync(respawnPacket, cancellationToken);
        }
        else
            await link.SendPacketAsync(joinGamePacket, cancellationToken);

        return true;
    }

    protected override async ValueTask<AuthenticationResult> HandleServerPacketAsync(ILink link, IMinecraftClientboundPacket packet, CancellationToken cancellationToken)
    {
        switch (packet)
        {
            case DisconnectPacket disconnectPacket:
                logger.LogInformation("Player {Player} cannot authenticate on {Server}: {Reason}", link.Player, link.Server, disconnectPacket.Reason);

                // Since the IPlayer client has already completed the login state, it cannot be kicked with a login disconnect packet
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

                await link.SendPacketAsync(new LoginPluginResponsePacket
                {
                    MessageId = loginPluginRequest.MessageId,
                    Data = loginPluginMessage.Response ?? [],
                    Successful = loginPluginMessage.Successful
                }, cancellationToken);
                break;
            case EncryptionRequestPacket:
                throw new InvalidOperationException("Authentication side is set to Proxy, but server is in online-mode.");
            default:
                throw new InvalidOperationException($"Unexpected {packet} packet received");
        }

        return AuthenticationResult.NoResult;
    }
}
