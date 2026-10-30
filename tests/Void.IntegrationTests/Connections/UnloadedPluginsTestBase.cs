using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The proxy unloads all its containers at once and loads all of them again as it does when it starts, after which a game plays through it.
/// </summary>
public abstract class UnloadedPluginsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var contexts = StartupContainers.Select(name => (Name: name, Context: TrackContext(name))).ToArray();

            await Plugins.UnloadContainersAsync(Timeouts.StepTimeoutToken);
            Assert.Empty(Plugins.Containers);

            foreach (var (name, context) in contexts)
                await AssertCollectedAsync(name, context, Timeouts.StepTimeoutToken);

            await Plugins.LoadPluginsAsync(cancellationToken: Timeouts.StepTimeoutToken);
            Assert.Equal(StartupContainers.Order(), Plugins.Containers.Order());

            await PlayAsync(nameof(UnloadedPluginsTestBase));
        });
    }
}
