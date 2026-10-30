using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// A proxy of its own reloads every container and then stops, which unloads them all and exits cleanly, and a proxy started again in the same
/// working directory, with the settings the reloaded containers left, plays a game through it.
/// </summary>
public abstract class ProxyRestartPluginReloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected async Task RunAsync()
    {
        Fixture.SkipIfUnavailable();

        var workingDirectory = Path.Combine(Path.GetTempPath(), nameof(ProxyRestartPluginReloadTestBase), Path.GetRandomFileName());
        string[] targetServers = [.. Fixture.Servers.Select(server => $"localhost:{server.Port}")];

        await using (var reloadedProxy = await VoidProxy.CreateAsync(workingDirectory, targetServers, cancellationToken: Timeouts.SetupTimeoutToken))
        {
            await LoggedExecutorAsync(async () =>
            {
                foreach (var name in reloadedProxy.StartupContainers)
                {
                    await reloadedProxy.Plugins.UnloadContainerAsync(name, Timeouts.StepTimeoutToken);
                    await reloadedProxy.Plugins.LoadContainerAsync(name, Timeouts.StepTimeoutToken);
                }

                Assert.Equal(reloadedProxy.StartupContainers.Order(), reloadedProxy.Plugins.Containers.Order());

                await reloadedProxy.LogWriter.WriteLineAsync($"Stopping {nameof(VoidProxy)} after its plugins reloaded");
                await reloadedProxy.DisposeAsync().AsTask().WaitAsync(Timeouts.StepTimeoutToken);

                Assert.Equal(0, reloadedProxy.RunResult.ExitCode);
            }, reloadedProxy);
        }

        await using var restartedProxy = await VoidProxy.CreateAsync(workingDirectory, targetServers, cancellationToken: Timeouts.SetupTimeoutToken);

        await LoggedExecutorAsync(async () =>
        {
            Assert.Equal(restartedProxy.StartupContainers.Order(), Fixture.VoidProxy.StartupContainers.Order());
            await PlayAsync(nameof(ProxyRestartPluginReloadTestBase), restartedProxy);
        }, Fixture.Game1, restartedProxy, Fixture.Server1, Fixture.Server2);
    }
}
