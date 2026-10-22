using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.Logging.Abstractions;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Events;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Events;

public class EventServiceTests
{
    [Fact]
    public async Task EventsReachListenersWhileOtherListenersComeAndGo()
    {
        // Players register and unregister their listeners on threads of their own, while events of other players are invoked.
        const int eventCount = 20_000;

        var container = new Container();
        container.RegisterInstance(StandIn.Create<IDependencyService>((_, _) => null));

        var events = new EventService(NullLogger<EventService>.Instance, container);
        var listener = new CountingListener();
        events.RegisterListeners(listener);

        using var churnCancellation = new CancellationTokenSource();
        var churn = Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!churnCancellation.IsCancellationRequested)
            {
                var otherListener = new CountingListener();
                events.RegisterListeners(otherListener);
                events.UnregisterListeners(otherListener);
            }
        })));

        try
        {
            await Parallel.ForAsync(0, eventCount, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (_, cancellationToken) => await events.ThrowAsync(new CountedEvent(), cancellationToken));
        }
        finally
        {
            await churnCancellation.CancelAsync();
            await churn;
        }

        Assert.Equal(eventCount, listener.Count);
    }

    public sealed record CountedEvent : IEvent;

    public sealed class CountingListener : IEventListener
    {
        private int _count;

        public int Count => _count;

        [Subscribe]
        public void OnCountedEvent(CountedEvent @event)
        {
            Interlocked.Increment(ref _count);
        }
    }
}
