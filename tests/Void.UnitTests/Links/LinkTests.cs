using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Network.Messages;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Servers;
using Void.Proxy.Links;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Links;

// The links fill the thread pool for a while, which would slow the tests running beside them past their timeouts
[CollectionDefinition(DisableParallelization = true)]
public class LinkTestsCollection;

[Collection(typeof(LinkTestsCollection))]
public class LinkTests
{
    [Fact]
    public async Task LinkWhosePlayerLeavesAsItStartsStops()
    {
        // A player that asked for the status of a server leaves as soon as it has it, which can be before its link finished starting, while the
        // server waits for more. Among many such links, some players leave before their link started reading the server.
        var links = Enumerable.Range(0, 50000).Select(_ => Task.Run(async () =>
        {
            var stopped = new TaskCompletionSource();
            var events = StandIn.Create<IEventService>((_, arguments) =>
            {
                if (arguments is [LinkStoppedEvent, ..])
                    stopped.TrySetResult();

                return null;
            });

            var link = new Link(StandIn.Create<IPlayer>((_, _) => null), StandIn.Create<IServer>((_, _) => null), Channel(left: true), Channel(left: false),
                NullLogger.Instance, events, CancellationToken.None);

            await link.StartAsync(TestContext.Current.CancellationToken);
            return await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)) == stopped.Task;
        }, TestContext.Current.CancellationToken));

        Assert.Equal(0, (await Task.WhenAll(links)).Count(stopped => !stopped));
    }

    private static INetworkChannel Channel(bool left)
    {
        return StandIn.Create<INetworkChannel>((method, arguments) => method.Name switch
        {
            "get_CanRead" or "get_CanWrite" => true,
            nameof(INetworkChannel.ReadMessageAsync) when left => ValueTask.FromException<INetworkMessage>(new StreamException()),
            nameof(INetworkChannel.ReadMessageAsync) => WaitForCancellationAsync(arguments.OfType<CancellationToken>().ToArray()),
            _ => null
        });
    }

    private static ValueTask<INetworkMessage> WaitForCancellationAsync(CancellationToken[] cancellationTokens)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens);
        return new ValueTask<INetworkMessage>(Task.Delay(Timeout.Infinite, cancellationTokenSource.Token).ContinueWith<INetworkMessage>(_ => throw new OperationCanceledException(), TaskScheduler.Default));
    }
}
