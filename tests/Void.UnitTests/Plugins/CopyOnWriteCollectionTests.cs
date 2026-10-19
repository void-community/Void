using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.Proxy.Plugins.Containers;
using Xunit;

namespace Void.UnitTests.Plugins;

public class CopyOnWriteCollectionTests
{
    [Fact]
    public void AddingWhileEnumeratingASnapshotDoesNotDisturbIt()
    {
        // A plugin's dependency lookup enumerates the containers while another plugin's load adds one (issue #1594).
        var collection = new CopyOnWriteCollection<int>();
        collection.Add(1);
        collection.Add(2);
        var seen = new System.Collections.Generic.List<int>();

        foreach (var item in collection.Snapshot)
        {
            seen.Add(item);
            collection.Add(item + 10);
        }

        Assert.Equal([1, 2], seen);
        Assert.Equal([1, 2, 11, 12], collection.Snapshot);
    }

    [Fact]
    public async Task KeepsEveryItemAddedConcurrentlyWhileOthersRead()
    {
        var collection = new CopyOnWriteCollection<int>();
        using var writersDone = new CancellationTokenSource();

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            long total = 0;

            while (!writersDone.IsCancellationRequested)
            {
                foreach (var item in collection.Snapshot)
                    total += item;
            }

            return total;
        }, TestContext.Current.CancellationToken)).ToArray();

        Parallel.For(0, 10_000, item => collection.Add(item));
        await writersDone.CancelAsync();
        await Task.WhenAll(readers);

        Assert.Equal(Enumerable.Range(0, 10_000), collection.Snapshot.Order());
    }

    [Fact]
    public void RemoveAllLeavesEarlierSnapshotsIntact()
    {
        var collection = new CopyOnWriteCollection<string>();
        collection.Add("unloaded");
        collection.Add("alive");
        var before = collection.Snapshot;

        collection.RemoveAll(item => item == "unloaded");

        Assert.Equal(["alive"], collection.Snapshot);
        Assert.Equal(["unloaded", "alive"], before);
    }
}
