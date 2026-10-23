using System;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The reasons a server gives for removing a player reach the game through the proxy: a kick of a player that plays, which the proxy passes
/// on, and the refusal of a player that joins, which the proxy turns into a kick of its own.
/// </summary>
public abstract class ProxiedKickTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        // Commands take a reason as their last words, which a GUID's hex digits keep to one.
        var kickReason = $"kicked-{Guid.NewGuid():N}";
        var banReason = $"banned-{Guid.NewGuid():N}";

        await LoggedExecutorAsync(async () =>
        {
            await using var game = await fixture.Game1.BeginSessionAsync(nameof(ProxiedKickTestBase), [fixture.VoidProxy, fixture.Server1], Timeouts.SetupTimeoutToken);

            var playerDisconnectionTask = fixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

            await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
            await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
            await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

            await fixture.Server1.RunCommandAsync($"kick {game.Username} {kickReason}", Timeouts.StepTimeoutToken);
            await game.ExpectKickAsync(kickReason, Timeouts.StepTimeoutToken);
            await playerDisconnectionTask;

            // An offline server bans a player by the name it saw join, as the player just did.
            await fixture.Server1.RunCommandAsync($"ban {game.Username} {banReason}", Timeouts.StepTimeoutToken);

            try
            {
                await game.JoinServerExpectingFailureAsync(ProxyEndPoint, banReason, Timeouts.StepTimeoutToken);
            }
            finally
            {
                await fixture.Server1.RunCommandAsync($"pardon {game.Username}", Timeouts.StepTimeoutToken);
            }

            await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
        }, fixture.Game1, fixture.VoidProxy, fixture.Server1);
    }
}
