using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Plugins.Dependencies;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Plugins;

public class DependencyServiceTests
{
    [Fact]
    public void PlayersJoiningAndLeavingAtOnceLeaveAPluginThatUnloads()
    {
        // Players join and leave on threads of their own, while the proxy may be shutting down and unloading plugins.
        var root = new Container();
        root.RegisterInstance(StandIn.Create<IEventService>((method, _) => method.Name is "get_Listeners" ? Array.Empty<IEventListener>() : null));

        var dependencies = new DependencyService(NullLogger<DependencyService>.Instance, root);
        var plugin = dependencies.CreateInstance<TestPlugin>();
        var playerNumber = 0;

        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, index =>
        {
            var context = PlayerContext(Interlocked.Increment(ref playerNumber));
            dependencies.ActivatePlayerScope(context);

            if (index % 2 is 0)
                dependencies.DisposePlayerScope(context);
        });

        dependencies.OnPluginUnloaded(new PluginUnloadedEvent(plugin));
    }

    private static IPlayerContext PlayerContext(int stableHashCode)
    {
        IPlayerContext? context = null;
        var player = StandIn.Create<IPlayer>((method, _) => method.Name switch
        {
            nameof(IPlayer.GetStableHashCode) => stableHashCode,
            "get_Client" => new TcpClient(),
            "get_Context" => context,
            _ => null
        });

        context = StandIn.Create<IPlayerContext>((method, _) => method.Name is "get_Player" ? player : null);
        return context;
    }

    // The players' scopes are opened in the container of each plugin, such as this one.
    public sealed class TestPlugin : IPlugin
    {
        public string Name => nameof(TestPlugin);
    }
}
