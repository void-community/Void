using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// A game stops a proxy of its own with the <c>/stop</c> command of Essentials, which kicks the game with why as it shuts down, and exits
/// cleanly. The game shows the kick rather than the command's answer, which it skips as the kick disconnects it right after.
/// </summary>
public abstract class EssentialsStopTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        var workingDirectory = Path.Combine(Path.GetTempPath(), nameof(EssentialsStopTestBase), Path.GetRandomFileName());
        string[] targetServers = [.. fixture.Servers.Select(server => $"localhost:{server.Port}")];

        await using var proxy = await VoidProxy.CreateAsync(workingDirectory, targetServers, cancellationToken: Timeouts.SetupTimeoutToken);

        await LoggedExecutorAsync(async () =>
        {
            await using (var game = await fixture.Game1.BeginSessionAsync(nameof(EssentialsStopTestBase), [proxy, fixture.Server1, fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                await game.JoinServerAsync(new IPEndPoint(IPAddress.Loopback, proxy.Port), Timeouts.SetupTimeoutToken);
                await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                var kickTask = game.ExpectKickAsync("Proxy is shutting down", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/stop", Timeouts.StepTimeoutToken);
                await kickTask;

                await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
            }

            await proxy.RunResult.CompletionTask.WaitAsync(Timeouts.StepTimeoutToken);
            Assert.Equal(0, proxy.RunResult.ExitCode);
        }, fixture.Game1, proxy, fixture.Server1, fixture.Server2);
    }
}
