using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
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

    [Fact]
    public async Task ServerMessageAfterListenerFoundServerClosedIsStillRead()
    {
        // A server that kicks a player sends why and closes, while a listener may still be answering what the server sent before, such as a keep-alive
        // that SlowJoin answers for the player. The answer finds the server closed, and the kick that follows is still read from the connection.
        var keepAlive = StandIn.Create<INetworkMessage>((_, _) => null);
        var kick = StandIn.Create<INetworkMessage>((_, _) => null);
        var serverMessages = new Queue<INetworkMessage>([keepAlive, kick]);
        var written = new List<INetworkMessage>();
        var stopped = new TaskCompletionSource();

        var events = StandIn.Create<IEventService>((_, arguments) => arguments switch
        {
            [MessageReceivedEvent { Message: var message }, ..] when message == keepAlive => ValueTask.FromException<bool>(new StreamClosedException()),
            [LinkStoppedEvent, ..] => Stopped(),
            _ => null
        });

        var serverChannel = StandIn.Create<INetworkChannel>((method, _) => method.Name switch
        {
            "get_CanRead" or "get_CanWrite" => true,
            nameof(INetworkChannel.ReadMessageAsync) => serverMessages.TryDequeue(out var message) ? ValueTask.FromResult(message) : ValueTask.FromException<INetworkMessage>(new StreamClosedException()),
            _ => null
        });

        var playerChannel = StandIn.Create<INetworkChannel>((method, arguments) => method.Name switch
        {
            "get_CanRead" or "get_CanWrite" => true,
            nameof(INetworkChannel.ReadMessageAsync) => WaitForCancellationAsync(arguments.OfType<CancellationToken>().ToArray()),
            nameof(INetworkChannel.WriteMessageAsync) when arguments is [INetworkMessage message, ..] => Write(message),
            _ => null
        });

        var link = new Link(StandIn.Create<IPlayer>((_, _) => null), StandIn.Create<IServer>((_, _) => null), playerChannel, serverChannel, NullLogger.Instance, events,
            CancellationToken.None);

        await link.StartAsync(TestContext.Current.CancellationToken);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal([kick], written);

        ValueTask Write(INetworkMessage message)
        {
            lock (written)
                written.Add(message);

            return ValueTask.CompletedTask;
        }

        object? Stopped()
        {
            stopped.TrySetResult();
            return null;
        }
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
