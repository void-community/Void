using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The container of the game's protocol unloads while the game plays through the proxy, which kicks the game with why, and collects the
/// container despite the player who played with it. Once it loads again, the game plays as before. As the container of mods unloads, a
/// modded game whose handshake it recorded is kicked with why, and one it recorded nothing for, as NeoForge since 1.20.5, plays on.
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

                using var kickCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Timeouts.StepTimeoutToken);
                var kickTask = game.ExpectKickAsync("Void unloaded support for your mods", kickCancellationTokenSource.Token);
                await UnloadAsync(modsContainer, Timeouts.StepTimeoutToken);

                // The kick goes out as the container unloads, so the game has either left by now or plays on
                if (Proxy.LogWriter.GetLinesSince(game.StartedAt).Any(line => line.Contains($"Player {game.Username} disconnected", StringComparison.Ordinal)))
                {
                    await kickTask;
                    await playerDisconnectionTask;
                    await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
                }
                else
                {
                    await kickCancellationTokenSource.CancelAsync();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kickTask);

                    var secondServerReadyTask = Proxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                    await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                    await secondServerReadyTask;

                    await game.EndAsync(Fixture.Server2, Timeouts.StepTimeoutToken);
                    await playerDisconnectionTask;
                }
            }

            await LoadAsync(modsContainer, Timeouts.StepTimeoutToken);
            await PlayAsync(nameof(ProxiedProtocolUnloadTestBase));
        });
    }
}
