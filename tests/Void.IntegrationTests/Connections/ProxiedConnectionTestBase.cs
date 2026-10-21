using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Extensions;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Connections;

public abstract class ProxiedConnectionTestBase(ProtocolVersion protocolVersion, IMinecraftServersFixture serversFixture, IVoidFixture voidFixture, PortableMinecraftClientFixture portableMinecraftClientFixture) : IntegrationUnitBase, IClassFixture<PortableMinecraftClientFixture>
{
    private const string ExpectedText = "hello proxied void!";

    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, voidFixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        serversFixture.SkipIfUnavailable();

        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(ProxiedConnectionTestBase), protocolVersion, serversFixture.Setup, [voidFixture.VoidProxy, serversFixture.Server1], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = voidFixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await serversFixture.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await serversFixture.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(serversFixture.Server1.Logs, line => line.Contains(expectedText));
        }, portableMinecraftClientFixture.Api, voidFixture.VoidProxy, serversFixture.Server1);
    }
}
