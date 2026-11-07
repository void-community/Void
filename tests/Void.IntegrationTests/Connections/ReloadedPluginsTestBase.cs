using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Every container reloads one after another, each collected once it unloaded, and a game then plays through the proxy on both servers as on
/// a proxy that never reloaded.
/// </summary>
public abstract class ReloadedPluginsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            foreach (var name in StartupContainers)
                await ReloadAsync(name, Timeouts.StepTimeoutToken);

            await PlayAsync(nameof(ReloadedPluginsTestBase), redirect: true);
        });
    }
}
