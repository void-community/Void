using System;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class ProxiedConnectionTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private const string ExpectedText = "hello proxied void!";

    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await fixture.Game1.BeginSessionAsync(nameof(ProxiedConnectionTestBase), [fixture.VoidProxy, fixture.Server1], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = fixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await fixture.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(fixture.Server1.Logs, line => line.Contains(expectedText));
        }, fixture.Game1, fixture.VoidProxy, fixture.Server1);
    }
}
