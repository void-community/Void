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

public abstract class DirectConnectionTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private const string ExpectedText = "hello void!";

    private EndPoint ServerEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.Server1.Port);

    protected async Task RunAsync()
    {
        if (!PortableMinecraftClient.SupportedVersions.Contains(fixture.ProtocolVersion))
            Assert.Skip($"Protocol version {fixture.ProtocolVersion} is not supported by the client, skipping test.");

        fixture.SkipIfUnavailable();

        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            await using (var game = await fixture.Game1.BeginSessionAsync(nameof(DirectConnectionTestBase), [fixture.Server1], Timeouts.SetupTimeoutToken))
            {
                await game.JoinServerAsync(ServerEndPoint, Timeouts.SetupTimeoutToken);
                await fixture.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await fixture.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);

                await game.EndAsync(fixture.Server1, Timeouts.StepTimeoutToken);
            }

            Assert.Contains(fixture.Server1.Logs, line => line.Contains(expectedText));
        }, fixture.Game1, fixture.Server1);
    }
}
