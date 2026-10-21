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

    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, serversFixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        var protocolVersion = serversFixture.ProtocolVersion;

        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        serversFixture.SkipIfUnavailable();

        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(ProxiedConnectionTestBase), protocolVersion, serversFixture.Servers.Setup, [serversFixture.VoidProxy, serversFixture.Servers.Server1], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = serversFixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await serversFixture.Servers.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await serversFixture.Servers.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(serversFixture.Servers.Server1.Logs, line => line.Contains(expectedText));
        }, portableMinecraftClientFixture.Api, serversFixture.VoidProxy, serversFixture.Servers.Server1);
    }
}
