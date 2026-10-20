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

public abstract class DirectConnectionTestBase(IMinecraftServersFixture serversFixture, PortableMinecraftClientFixture portableMinecraftClientFixture) : IntegrationUnitBase, IClassFixture<PortableMinecraftClientFixture>
{
    private const string ExpectedText = "hello void!";

    protected async Task RunAsync(ProtocolVersion protocolVersion)
    {
        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        var servers = await serversFixture.PrepareServersAsync(protocolVersion, serverCount: 1, Timeouts.SetupTimeoutToken);
        var serverEndPoint = new IPEndPoint(IPAddress.Loopback, servers.Server1.Port);
        var expectedText = $"{ExpectedText} test #{Random.Shared.Next()}";

        await LoggedExecutorAsync(async () =>
        {
            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(DirectConnectionTestBase), protocolVersion, servers.Setup, [servers.Server1], Timeouts.SetupTimeoutToken))
            {
                await game.JoinServerAsync(serverEndPoint, Timeouts.SetupTimeoutToken);
                await servers.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(expectedText, Timeouts.StepTimeoutToken);
                await servers.Server1.ExpectTextAsync(expectedText, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            Assert.Contains(servers.Server1.Logs, line => line.Contains(expectedText));
        }, portableMinecraftClientFixture.Api, servers.Server1);
    }
}
