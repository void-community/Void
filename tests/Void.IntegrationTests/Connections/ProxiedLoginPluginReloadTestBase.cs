using System;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Every container but the one of the game's protocol reloads while the game joins through the proxy, from the moment the proxy first sees
/// its name, and the game joins and plays as on a proxy that didn't reload.
/// </summary>
public abstract class ProxiedLoginPluginReloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var containers = FindContainersBesidesProtocol();
            var message = $"joined-{Guid.NewGuid():N}";
            Task playerDisconnectionTask;

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedLoginPluginReloadTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                // The player's name reaches the proxy with Login Start, before the proxy chose a server or logged the player in to it.
                var loginStartedTask = Proxy.LogWriter.WaitForLineAsync(line => line.Contains(game.Username, StringComparison.Ordinal), Timeouts.SetupTimeoutToken);
                var joinTask = game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);

                await loginStartedTask;

                foreach (var name in containers)
                    await ReloadAsync(name, Timeouts.StepTimeoutToken);

                await joinTask;
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(message, Timeouts.StepTimeoutToken);
                await Fixture.Server1.ExpectTextAsync(message, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(Fixture.Server1, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;
        });
    }
}
