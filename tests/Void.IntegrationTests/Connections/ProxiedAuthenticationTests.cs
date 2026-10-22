using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Connections;

public class ProxiedAuthenticationTests : IntegrationUnitBase
{
    private const string ExpectedKickReason = "The server is full!";
    private static readonly ProtocolVersion TestProtocolVersion = ProtocolVersion.MINECRAFT_1_21_4;

    [Fact]
    public async Task PaperServerFullKickIsRelayedToPlayerAsync()
    {
        await using var client = new ReusableGame(TestProtocolVersion, setup: null);
        await using var paperServer = await PaperServer.CreateAsync("server-full.log", Timeouts.SetupTimeoutToken, maximumPlayers: 0);
        var voidProxy = await VoidProxy.CreateAsync(
            Path.Combine(Path.GetTempPath(), nameof(ProxiedAuthenticationTests), Path.GetRandomFileName()),
            $"localhost:{paperServer.Port}",
            cancellationToken: Timeouts.SetupTimeoutToken);
        var proxyEndPoint = new IPEndPoint(IPAddress.Loopback, voidProxy.Port);
        var voidLogWindowStartedAt = DateTime.UtcNow;

        await using (voidProxy)
        {
            await LoggedExecutorAsync(async () =>
            {
                await using var game = await client.BeginSessionAsync(nameof(ProxiedAuthenticationTests), [voidProxy, paperServer], Timeouts.SetupTimeoutToken);
                var authenticationFailureTask = voidProxy.LogWriter.WaitForLineAsync(
                    line => line.Contains($"Player {game.Username} cannot authenticate on args-server-1: {{text:\"{ExpectedKickReason}\"}}", StringComparison.Ordinal),
                    Timeouts.SetupTimeoutToken);
                var playerDisconnectionTask = voidProxy.LogWriter.WaitForLineAsync(
                    line => line.Contains($"Player {game.Username} disconnected", StringComparison.Ordinal),
                    Timeouts.SetupTimeoutToken);
                var paperRejectionTask = paperServer.Container.ExpectTextAsync(ExpectedKickReason, game.StartedAt, Timeouts.SetupTimeoutToken);

                await game.JoinServerExpectingFailureAsync(proxyEndPoint, ExpectedKickReason, Timeouts.SetupTimeoutToken);

                await authenticationFailureTask;
                await paperRejectionTask;
                await playerDisconnectionTask;

                await game.EndAsync(joinedServer: null, Timeouts.StepTimeoutToken);
            }, client, voidProxy, paperServer);
        }

        voidProxy.AssertNoWarningOrHigherLogsSince(voidLogWindowStartedAt);
    }
}
