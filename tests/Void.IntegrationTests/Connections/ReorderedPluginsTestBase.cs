using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The containers unload one after another and load again in the same order, whatever order it is: the one they loaded in when the proxy
/// started, which unloads a dependency before what depends on it, the reverse one, and a random one. A game then plays through the proxy.
/// </summary>
public abstract class ReorderedPluginsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var seed = Random.Shared.Next();
            var random = new Random(seed);

            IEnumerable<(string Name, string[] Containers)> orders =
            [
                ("startup", [.. StartupContainers]),
                ("reverse", [.. StartupContainers.Reverse()]),
                ($"random with seed {seed}", [.. StartupContainers.OrderBy(name => random.Next())])
            ];

            foreach (var (order, containers) in orders)
            {
                Console.WriteLine($"[Void.IntegrationTests] Reloading plugin containers in {order} order: {string.Join(", ", containers)}");

                foreach (var name in containers)
                    await UnloadAsync(name, Timeouts.StepTimeoutToken);

                foreach (var name in containers)
                    await LoadAsync(name, Timeouts.StepTimeoutToken);
            }

            await PlayAsync(nameof(ReorderedPluginsTestBase));
        });
    }
}
