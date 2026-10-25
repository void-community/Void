using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The container of the game's protocol unloads while the game plays through the proxy, which kicks the game with why, and collects the
/// container despite the player who played with it. Once it loads again, the game plays as before. A modded game is kicked with why as the
/// container of its mods unloads too.
/// </summary>
public abstract class ProxiedProtocolUnloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var protocolContainer = FindProtocolContainer();

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedProtocolUnloadTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                var playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                var kickTask = game.ExpectKickAsync($"Void unloaded support for your Minecraft version ({Fixture.ProtocolVersion})", Timeouts.StepTimeoutToken);
                await UnloadAsync(protocolContainer, Timeouts.StepTimeoutToken);
                await kickTask;
                await playerDisconnectionTask;

                await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
            }

            await LoadAsync(protocolContainer, Timeouts.StepTimeoutToken);
            await PlayAsync(nameof(ProxiedProtocolUnloadTestBase));

            if (Fixture is not ModdedConnectionsFixture)
                return;

            var modsContainer = FindModsContainer();

            await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(ProxiedProtocolUnloadTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
            {
                var playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                var kickTask = game.ExpectKickAsync("Void unloaded support for your mods", Timeouts.StepTimeoutToken);
                await UnloadAsync(modsContainer, Timeouts.StepTimeoutToken);
                await kickTask;
                await playerDisconnectionTask;

                await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
            }

            await LoadAsync(modsContainer, Timeouts.StepTimeoutToken);
            await PlayAsync(nameof(ProxiedProtocolUnloadTestBase));
        });
    }
}
