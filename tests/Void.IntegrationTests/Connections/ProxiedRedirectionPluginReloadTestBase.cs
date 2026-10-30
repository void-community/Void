using System;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Every container but the one of the game's protocol reloads while the game moves to the other server through the proxy, from the moment the
/// proxy runs the game's command, and the game arrives and plays there.
/// </summary>
public abstract class ProxiedRedirectionPluginReloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var containers = FindContainersBesidesProtocol();
            var message = $"moved-{Guid.NewGuid():N}";
            Task playerDisconnectionTask;

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedRedirectionPluginReloadTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                var commandEnteredTask = Proxy.LogWriter.WaitForLineAsync(line => line.Contains("Entered command: server args-server-2", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
                var secondServerReadyTask = Proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.SetupTimeoutToken);
                var commandTask = game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);

                await commandEnteredTask;

                foreach (var name in containers)
                    await ReloadAsync(name, Timeouts.StepTimeoutToken);

                await commandTask;
                await secondServerReadyTask;

                await game.SendTextMessageAsync(message, Timeouts.StepTimeoutToken);
                await Fixture.Server2.ExpectTextAsync(message, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(Fixture.Server2, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;
        });
    }
}
