using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class ProxiedServerRedirectionTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        if (!PortableMinecraftClient.SupportedVersions.Contains(fixture.ProtocolVersion))
            Assert.Skip($"Protocol version {fixture.ProtocolVersion} is not supported by the client, skipping test.");

        fixture.SkipIfUnavailable();

        var firstMessage = $"server1-{Guid.NewGuid()}";
        var secondMessage = $"server2-{Guid.NewGuid()}";
        var thirdMessage = $"server1-{Guid.NewGuid()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;
            string username;
            DateTime startedAt;

            await using (var game = await fixture.Game1.BeginSessionAsync(nameof(ProxiedServerRedirectionTestBase), [fixture.VoidProxy, fixture.Server1, fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                username = game.Username;
                startedAt = game.StartedAt;
                playerDisconnectionTask = fixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await fixture.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(firstMessage, Timeouts.StepTimeoutToken);
                await fixture.Server1.ExpectTextAsync(firstMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var secondServerReadyTask = fixture.VoidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.SendTextMessageAsync(secondMessage, Timeouts.StepTimeoutToken);
                await fixture.Server2.ExpectTextAsync(secondMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var firstServerReadyTask = fixture.VoidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-1", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-1", Timeouts.StepTimeoutToken);
                await firstServerReadyTask;

                await game.SendTextMessageAsync(thirdMessage, Timeouts.StepTimeoutToken);
                await fixture.Server1.ExpectTextAsync(thirdMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(fixture.Server1.Logs, line => line.Contains(firstMessage));
            Assert.Contains(fixture.Server2.Logs, line => line.Contains(secondMessage));
            Assert.Contains(fixture.Server1.Logs, line => line.Contains(thirdMessage));

            // The proxy and the player outlive the test, so only what the player did in it counts.
            var proxyLogs = fixture.VoidProxy.LogWriter.GetLinesSince(startedAt).Where(line => line.Contains(username, StringComparison.Ordinal)).ToArray();

            Assert.Contains(proxyLogs, line => line.Contains("connected to args-server-2"));
            Assert.True(proxyLogs.Count(line => line.Contains("connected to args-server-1")) is >= 2); // TODO: sometimes, proxy prints multiple times "connected to" message
        }, fixture.Game1, fixture.VoidProxy, fixture.Server1, fixture.Server2);
    }
}
