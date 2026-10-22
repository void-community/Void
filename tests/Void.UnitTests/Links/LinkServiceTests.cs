using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events.Authentication;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Servers;
using Void.Proxy.Links;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Links;

public class LinkServiceTests
{
    [Fact]
    public async Task ServerThatClosesTheConnectionOfAJoiningPlayerIsJoinedOnceMore()
    {
        // A server can drop a player that joins it by a race of its own, which another try usually passes.
        var test = new Proxy(closingAuthentications: 1);

        var result = await test.Links.ConnectAsync(test.Player, test.Server, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionResult.Connected, result);
        Assert.Equal(2, test.ServerConnections);
    }

    [Fact]
    public async Task ServerThatClosesTheConnectionAgainIsLeft()
    {
        var test = new Proxy(closingAuthentications: 2);

        var result = await test.Links.ConnectAsync(test.Player, test.Server, TestContext.Current.CancellationToken);

        Assert.Equal(ConnectionResult.NotConnected, result);
        Assert.Equal(2, test.ServerConnections);
    }

    /// <summary>
    /// A link service with stand-ins around it, and a player joining a server that closes the connection while some of its authentications run.
    /// </summary>
    private sealed class Proxy
    {
        private int _authentications;

        public Proxy(int closingAuthentications)
        {
            var playerChannel = StandIn.Create<INetworkChannel>((method, _) => method.Name is "get_IsAlive" ? true : null);

            var channelBuilder = StandIn.Create<IChannelBuilderService>((method, _) =>
            {
                if (method.Name is not nameof(IChannelBuilderService.BuildServerChannelAsync))
                    return null;

                ServerConnections++;
                return ValueTask.FromResult(StandIn.Create<INetworkChannel>((_, _) => null));
            });

            var events = StandIn.Create<IEventService>((_, arguments) => arguments[0] switch
            {
                CreateLinkEvent => ValueTask.FromResult<ILink?>(StandIn.Create<ILink>((linkMethod, _) => linkMethod.Name is "get_Player" ? Player : null)),
                AuthenticationStartingEvent => ValueTask.FromResult(AuthenticationSide.Server),
                AuthenticationStartedEvent => ++_authentications <= closingAuthentications
                    ? ValueTask.FromException<AuthenticationResult?>(new StreamClosedException())
                    : ValueTask.FromResult<AuthenticationResult?>(AuthenticationResult.Authenticated),
                _ => null
            });

            var services = StandIn.Create<IServiceProvider>((_, arguments) => arguments[0] switch
            {
                Type type when type == typeof(IChannelBuilderService) => channelBuilder,
                Type type when type == typeof(IEventService) => events,
                Type type when type == typeof(IPlayerService) => StandIn.Create<IPlayerService>((_, _) => null),
                Type type when type == typeof(ILinkService) => Links,
                _ => null
            });

            var context = StandIn.Create<IPlayerContext>((method, _) => method.Name switch
            {
                "get_Channel" => playerChannel,
                "get_Services" => services,
                _ => null
            });

            Player = StandIn.Create<IPlayer>((method, _) => method.Name switch
            {
                "get_Context" => context,
                "get_Name" => "Steve",
                _ => null
            });

            Links = new LinkService(NullLogger<LinkService>.Instance, StandIn.Create<IServerService>((_, _) => null), events, StandIn.Create<IHostApplicationLifetime>((_, _) => null));
        }

        public LinkService Links { get; }

        public IPlayer Player { get; }

        public IServer Server { get; } = StandIn.Create<IServer>((method, _) => method.Name is "get_Name" ? "args-server-1" : null);

        public int ServerConnections { get; private set; }
    }
}
