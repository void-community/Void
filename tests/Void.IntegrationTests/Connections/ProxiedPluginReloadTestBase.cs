using System;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Every container but the one of the game's protocol reloads while the game plays through the proxy, and the game keeps playing: its chat
/// reaches its server once, and it moves to the other server with the reloaded command.
/// </summary>
public abstract class ProxiedPluginReloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var containers = FindContainersBesidesProtocol();
            var message = $"reloaded-{Guid.NewGuid():N}";
            Task playerDisconnectionTask;

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedPluginReloadTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                foreach (var name in containers)
                    await ReloadAsync(name, Timeouts.StepTimeoutToken);

                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);
                await game.SendTextMessageAsync(message, Timeouts.StepTimeoutToken);
                await Fixture.Server1.ExpectTextAsync(message, lookupHistory: true, Timeouts.StepTimeoutToken);

                var secondServerReadyTask = Proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.EndAsync(Fixture.Server2, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Single(Fixture.Server1.Logs, line => line.Contains(message, StringComparison.Ordinal));
        });
    }
}
