using System;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class DirectConnectionTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private const string ExpectedText = "hello void!";

    private EndPoint ServerEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.Server1.Port);

    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        await RetryAfterServerRaceAsync(() => LoggedExecutorAsync(async () =>
        {
            var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

            await using (var game = await fixture.Game1.BeginSessionAsync(nameof(DirectConnectionTestBase), [fixture.Server1], Timeouts.SetupTimeoutToken))
            {
                await game.JoinServerAsync(ServerEndPoint, Timeouts.SetupTimeoutToken);
                await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await fixture.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken);
            }

            Assert.Contains(fixture.Server1.Logs, line => line.Contains(expectedText));
        }, fixture.Game1, fixture.Server1), fixture.Servers);
    }
}
