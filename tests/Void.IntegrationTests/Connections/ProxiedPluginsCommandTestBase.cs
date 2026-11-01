using System;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// A game reloads Essentials with the proxy's <c>/plugins</c> command from its chat, which answers there, and moves to the other server with
/// the command that the reloaded Essentials registered again.
/// </summary>
public abstract class ProxiedPluginsCommandTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var essentials = Assert.Single(StartupContainers, name => name.Contains("Essentials", StringComparison.Ordinal));
            var context = TrackContext(essentials);
            Task playerDisconnectionTask;

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedPluginsCommandTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync("/plugins reload essentials", Timeouts.StepTimeoutToken);
                await game.ExpectChatAsync($"Reloaded {essentials}", Timeouts.StepTimeoutToken);
                await AssertCollectedAsync(essentials, context, Timeouts.StepTimeoutToken);

                var secondServerReadyTask = Proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.EndAsync(Fixture.Server2, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;
        });
    }
}
