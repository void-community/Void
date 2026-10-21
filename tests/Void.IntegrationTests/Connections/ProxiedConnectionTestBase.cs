using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class ProxiedConnectionTestBase(IMinecraftServersFixture serversFixture, PortableMinecraftClientFixture portableMinecraftClientFixture) : IntegrationUnitBase, IClassFixture<PortableMinecraftClientFixture>
{
    private const string ExpectedText = "hello proxied void!";

    protected async Task RunAsync()
    {
        var protocolVersion = serversFixture.ProtocolVersion;

        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        serversFixture.SkipIfUnavailable();

        var servers = serversFixture.Servers;
        var voidProxy = serversFixture.VoidProxy;
        var proxyEndPoint = new IPEndPoint(IPAddress.Loopback, voidProxy.Port);
        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(ProxiedConnectionTestBase), protocolVersion, servers.Setup, [voidProxy, servers.Server1], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = voidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(proxyEndPoint, Timeouts.SetupTimeoutToken);
                await servers.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await servers.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(servers.Server1.Logs, line => line.Contains(expectedText));
        }, portableMinecraftClientFixture.Api, voidProxy, servers.Server1);
    }
}
