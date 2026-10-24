using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Every container reloads several times over, and no cycle leaves a load context or memory behind: each context of an earlier cycle is
/// collected, the proxy holds as many containers as it started with, and its memory stays where the first cycle left it.
/// </summary>
public abstract class RepeatedlyReloadedPluginsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    private const int Cycles = 5;

    // Leaked contexts hold their assemblies and plugins, megabytes a cycle, while the proxy's logs of each cycle stay well below this.
    private const long MaximumMemoryGrowth = 64L * 1024 * 1024;

    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var contexts = new List<(string Name, WeakReference Context)>();
            long? firstCycleMemory = null;

            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                foreach (var name in StartupContainers)
                {
                    contexts.Add((name, TrackContext(name)));
                    await ReloadAsync(name, Timeouts.StepTimeoutToken);
                }

                foreach (var (name, context) in contexts)
                    await AssertCollectedAsync(name, context, Timeouts.StepTimeoutToken);

                Assert.Equal(StartupContainers.Order(), Plugins.Containers.Order());

                var memory = GC.GetTotalMemory(forceFullCollection: true);
                firstCycleMemory ??= memory;

                Assert.True(memory - firstCycleMemory < MaximumMemoryGrowth, $"Memory grew by {(memory - firstCycleMemory) / 1024 / 1024} MiB over {cycle} reload cycles");
            }
        });
    }
}
