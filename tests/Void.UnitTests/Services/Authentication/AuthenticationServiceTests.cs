using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Players;
using Void.Minecraft.Players.Extensions;
using Void.Minecraft.Profiles;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Plugins.Common.Events;
using Void.Proxy.Plugins.Common.Services.Authentication;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Services.Authentication;

public class AuthenticationServiceTests
{
    [Fact]
    public async Task PlayerThatStopsLoggingInAfterLoginStartLeaves()
    {
        // Login Start gives the player its profile, and a player with a profile counts as logged in when the proxy tries the next server.
        var channelClosed = false;
        var channel = StandIn.Create<INetworkChannel>((method, _) =>
        {
            if (method.Name is nameof(INetworkChannel.Close))
                channelClosed = true;

            return method.Name is "get_IsAlive" ? !channelClosed : null;
        });

        var player = new MinecraftPlayer(new TcpClient(), StandIn.Create<IPlayerContext>((_, _) => null), remoteEndPoint: "127.0.0.1:50000", DateTimeOffset.UtcNow, ProtocolVersion.MINECRAFT_1_20_2);
        var link = StandIn.Create<ILink>((method, _) => method.Name switch
        {
            "get_Player" => player,
            "get_PlayerChannel" => channel,
            _ => null
        });

        // The player never answers the encryption request.
        var events = StandIn.Create<IEventService>((method, arguments) => arguments is [PlayerVerifyingEncryptionEvent, ..] ? throw new StreamTimeoutException(Operation.Read) : null);
        var service = new LoginStartOnlyAuthenticationService(events);

        await Assert.ThrowsAsync<StreamTimeoutException>(() => service.AuthenticateAsync(link).AsTask());

        Assert.True(channelClosed);
    }

    private sealed class LoginStartOnlyAuthenticationService(IEventService events) : AbstractAuthenticationService(
        events,
        StandIn.Create<IPlayerService>((_, _) => null),
        StandIn.Create<IServerService>((_, _) => null),
        StandIn.Create<IConsoleService>((_, _) => null),
        StandIn.Create<IDependencyService>((_, _) => null))
    {
        public ValueTask<AuthenticationResult> AuthenticateAsync(ILink link)
        {
            return AuthenticatePlayerAsync(link, CancellationToken.None);
        }

        protected override bool IsSupportedVersion(ProtocolVersion version)
        {
            return true;
        }

        protected override ValueTask<bool> IsPlayerAuthenticatedAsync(IPlayer player, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(false);
        }

        protected override ValueTask<bool> IsPlayerPlayingAsync(IPlayer player, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(false);
        }

        protected override ValueTask<bool> StartPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
        {
            link.Player.Profile = new GameProfile("Notch");
            return ValueTask.FromResult(true);
        }

        protected override ValueTask FinishPlayerLoginAsync(ILink link, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        protected override ValueTask HandshakeWithServerAsync(ILink link, IMinecraftServerboundPacket? packet, int nextState, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        protected override ValueTask StartServerLoginAsync(ILink link, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<AuthenticationResult> HandleServerPacketAsync(ILink link, IMinecraftClientboundPacket packet, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(AuthenticationResult.Authenticated);
        }
    }
}
