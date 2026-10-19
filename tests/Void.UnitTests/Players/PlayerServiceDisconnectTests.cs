using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Api.Servers;
using Void.Proxy.Api.Settings;
using Void.Proxy.Players;
using Xunit;

namespace Void.UnitTests.Players;

public class PlayerServiceDisconnectTests
{
    [Fact]
    public async Task PlayerWhoLeavesWhileMovingIsDisconnectedOnce()
    {
        // Issue #1595: the backend stopped, and the player quit while the proxy moved it to another server.
        var test = await Proxy.JoinAsync();
        test.Reconnect = () =>
        {
            test.ChannelAlive = false;
            return ValueTask.FromException<ConnectionResult>(new StreamClosedException());
        };

        await test.Service.OnLinkStopped(test.LinkStopped(LinkStopReason.ServerDisconnected), CancellationToken.None);

        Assert.Same(test.Player, Assert.Single(test.Disconnections).Player);
        Assert.Empty(test.Service.All);
        Assert.Empty(test.Kicks);

        await test.Service.OnLinkStopped(test.LinkStopped(LinkStopReason.PlayerDisconnected), CancellationToken.None);
        Assert.Single(test.Disconnections);
    }

    [Fact]
    public async Task PlayerStillConnectedAfterAFailedMoveIsKicked()
    {
        var test = await Proxy.JoinAsync();
        test.Reconnect = () => ValueTask.FromException<ConnectionResult>(new InvalidOperationException("synthetic failure"));

        await test.Service.OnLinkStopped(test.LinkStopped(LinkStopReason.ServerDisconnected), CancellationToken.None);

        Assert.Single(test.Kicks);
        Assert.Same(test.Player, Assert.Single(test.Disconnections).Player);
        Assert.Empty(test.Service.All);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstConnectionThatFailsDoesNotKeepThePlayer(bool hasChannel)
    {
        var test = await Proxy.JoinAsync(hasChannel, () => ValueTask.FromException<ConnectionResult>(new StreamClosedException()), channelAlive: false);

        Assert.Same(test.Player, Assert.Single(test.Disconnections).Player);
        Assert.Empty(test.Service.All);
        Assert.Empty(test.Kicks);
    }

    [Fact]
    public async Task ShutdownStillCancelsTheMove()
    {
        var test = await Proxy.JoinAsync();
        using var shutdown = new CancellationTokenSource();
        test.Reconnect = () =>
        {
            shutdown.Cancel();
            return ValueTask.FromException<ConnectionResult>(new OperationCanceledException(shutdown.Token));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.Service.OnLinkStopped(test.LinkStopped(LinkStopReason.ServerDisconnected), shutdown.Token).AsTask());
        Assert.Empty(test.Disconnections);
    }

    /// <summary>
    /// A player service with stand-ins for everything around it, and one player that has joined through it.
    /// </summary>
    private sealed class Proxy
    {
        private readonly List<object> _events = [];
        private INetworkChannel? _channel;

        private Proxy()
        {
            Player = StandIn.Create<IPlayer>(
                (method, _) => method.Name switch
                {
                    "get_Client" => Client,
                    "get_Context" => Context,
                    "get_Name" => "Steve",
                    _ => null
                }
            );

            Context = StandIn.Create<IPlayerContext>(
                (method, arguments) =>
                {
                    if (method.Name is "set_Channel")
                        _channel = (INetworkChannel?)arguments[0];

                    return method.Name switch
                    {
                        "get_Channel" => _channel,
                        "get_Player" => Player,
                        "get_Logger" => NullLogger.Instance,
                        _ => null
                    };
                }
            );

            var links = StandIn.Create<ILinkService>(
                (method, arguments) => method.Name is "ConnectAnywhereAsync"
                    ? arguments.Length is 2 ? FirstConnection() : Reconnect()
                    : null
            );

            var events = StandIn.Create<IEventService>(
                (method, arguments) =>
                {
                    switch (arguments[0])
                    {
                        case PlayerConnectingEvent:
                            return Activator.CreateInstance(method.ReturnType, Player);
                        default:
                            _events.Add(arguments[0]!);
                            return null;
                    }
                }
            );

            Service = new PlayerService(NullLogger<PlayerService>.Instance, StandIn.Create<IDependencyService>((_, _) => null), links, events, StandIn.Create<ISettings>((_, _) => null));
        }

        public PlayerService Service { get; }

        public IPlayer Player { get; }

        public IPlayerContext Context { get; }

        public TcpClient Client { get; } = new();

        public bool ChannelAlive { get; set; } = true;

        public Func<ValueTask<ConnectionResult>> FirstConnection { get; set; } = () => ValueTask.FromResult(ConnectionResult.Connected);

        public Func<ValueTask<ConnectionResult>> Reconnect { get; set; } = () => ValueTask.FromResult(ConnectionResult.Connected);

        public IEnumerable<PlayerDisconnectedEvent> Disconnections => _events.OfType<PlayerDisconnectedEvent>();

        public IEnumerable<PlayerKickEvent> Kicks => _events.OfType<PlayerKickEvent>();

        public static async Task<Proxy> JoinAsync(bool hasChannel = true, Func<ValueTask<ConnectionResult>>? firstConnection = null, bool channelAlive = true)
        {
            var proxy = new Proxy { ChannelAlive = channelAlive };
            proxy.FirstConnection = firstConnection ?? proxy.FirstConnection;

            if (hasChannel)
                proxy.Context.Channel = proxy.CreateChannel();

            await proxy.Service.AcceptPlayerAsync(proxy.Client, TestContext.Current.CancellationToken);
            return proxy;
        }

        public LinkStoppedEvent LinkStopped(LinkStopReason reason)
        {
            var server = StandIn.Create<IServer>((_, _) => null);
            var link = StandIn.Create<ILink>(
                (method, _) => method.Name switch
                {
                    "get_PlayerChannel" => _channel,
                    "get_Server" => server,
                    _ => null
                }
            );

            return new LinkStoppedEvent(link, Player, reason);
        }

        private INetworkChannel CreateChannel()
        {
            return StandIn.Create<INetworkChannel>(
                (method, _) =>
                {
                    if (method.Name is "Close")
                        ChannelAlive = false;

                    return method.Name is "get_IsAlive" ? ChannelAlive : null;
                }
            );
        }
    }

    /// <summary>
    /// Implements an interface by handing each call to a function, and answers default values for everything that function leaves out.
    /// </summary>
    public class StandIn : DispatchProxy
    {
        private Func<MethodInfo, object?[], object?> _handle = (_, _) => null;

        public static T Create<T>(Func<MethodInfo, object?[], object?> handle) where T : class
        {
            var standIn = DispatchProxy.Create<T, StandIn>();
            ((StandIn)(object)standIn)._handle = handle;
            return standIn;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name is nameof(Equals) && args is [var other])
                return ReferenceEquals(this, other);

            var returnType = targetMethod.ReturnType;
            return _handle(targetMethod, args ?? []) ?? (returnType.IsValueType && returnType != typeof(void) ? Activator.CreateInstance(returnType) : null);
        }
    }
}
