using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// A game plays with what Essentials does through the proxy: it logs the game joining its server with its version, and every message to and
/// from the game; it suggests servers and players to its commands; its <c>/server</c> moves the game to the other server and refuses the
/// console; and its <c>/kick</c> kicks the game, with the console's reason or with its own when the game kicks itself.
/// </summary>
public abstract class EssentialsCommandsTestBase(ConnectionsFixture fixture) : IntegrationUnitBase
{
    private const string DefaultKickReason = "You have been kicked by an operator.";

    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, fixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        fixture.SkipIfUnavailable();

        // Commands take a reason as their last words, which a GUID's hex digits keep to one.
        var kickReason = $"kicked-{Guid.NewGuid():N}";
        var message = $"server2-{Guid.NewGuid():N}";

        await LoggedExecutorAsync(async () =>
        {
            var proxy = fixture.VoidProxy;

            await using var game = await fixture.Game1.BeginSessionAsync(nameof(EssentialsCommandsTestBase), [proxy, fixture.Server1, fixture.Server2], Timeouts.SetupTimeoutToken);

            var playerDisconnectionTask = proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

            await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
            await fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
            await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

            var joinLogs = proxy.LogWriter.GetLinesSince(game.StartedAt);
            Assert.Contains(joinLogs, line => line.Contains($"Player {game.Username} connected to args-server-1 ({fixture.ProtocolVersion})", StringComparison.Ordinal));
            Assert.Contains(joinLogs, line => line.Contains($"from Client {game.Username}", StringComparison.Ordinal));
            Assert.Contains(joinLogs, line => line.Contains($"to Client {game.Username}", StringComparison.Ordinal));

            Assert.Contains("args-server-2", await SuggestAsync("server "));
            Assert.Contains(game.Username, await SuggestAsync("kick "));

            await proxy.RunConsoleCommandAsync("server args-server-2", line => line.Contains("This command can be executed only by a player", StringComparison.Ordinal), Timeouts.StepTimeoutToken);

            // Without a name, the game moves to the server it isn't on.
            var secondServerReadyTask = proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
            await game.SendTextMessageAsync("/server", Timeouts.StepTimeoutToken);
            await secondServerReadyTask;

            await game.SendTextMessageAsync(message, Timeouts.StepTimeoutToken);
            await fixture.Server2.ExpectTextAsync(message, lookupHistory: true, Timeouts.StepTimeoutToken);

            var kickTask = game.ExpectKickAsync(kickReason, Timeouts.StepTimeoutToken);
            await proxy.RunConsoleCommandAsync($"kick {game.Username} {kickReason}", line => line.Contains($"Player {game.Username} disconnected", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await kickTask;
            await playerDisconnectionTask;

            var rejoinedAt = DateTime.UtcNow;
            playerDisconnectionTask = proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

            await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
            await fixture.Server1.ExpectLoginAsync(game.Username, rejoinedAt, Timeouts.SetupTimeoutToken);
            await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

            kickTask = game.ExpectKickAsync(DefaultKickReason, Timeouts.StepTimeoutToken);
            await game.SendTextMessageAsync($"/kick {game.Username}", Timeouts.StepTimeoutToken);
            await kickTask;
            await playerDisconnectionTask;

            await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
        }, fixture.Game1, fixture.VoidProxy, fixture.Server1, fixture.Server2);
    }

    private async Task<string[]> SuggestAsync(string input)
    {
        var services = fixture.VoidProxy.RunResult.Services;
        var suggestions = await services.GetRequiredService<ICommandService>().SuggestAsync(input, services.GetRequiredService<IConsoleService>(), Timeouts.StepTimeoutToken);

        return [.. suggestions.Select(suggestion => suggestion.Text)];
    }
}
