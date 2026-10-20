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

public abstract class ProxiedServerRedirectionTestBase(IMinecraftServersFixture serversFixture, VoidFixture voidFixture, PortableMinecraftClientFixture portableMinecraftClientFixture) : IntegrationUnitBase, IClassFixture<VoidFixture>, IClassFixture<PortableMinecraftClientFixture>
{
    protected async Task RunAsync(ProtocolVersion protocolVersion)
    {
        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        var servers = await serversFixture.PrepareServersAsync(protocolVersion, serverCount: 2, Timeouts.SetupTimeoutToken);
        var voidProxy = await voidFixture.StartProxyAsync(servers, Timeouts.SetupTimeoutToken);
        var proxyEndPoint = new IPEndPoint(IPAddress.Loopback, voidProxy.Port);

        var firstMessage = $"server1-{Guid.NewGuid()}";
        var secondMessage = $"server2-{Guid.NewGuid()}";
        var thirdMessage = $"server1-{Guid.NewGuid()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(ProxiedServerRedirectionTestBase), protocolVersion, [voidProxy, servers.Server1, servers.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = voidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(proxyEndPoint, Timeouts.SetupTimeoutToken);
                await servers.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(firstMessage, Timeouts.StepTimeoutToken);
                await servers.Server1.ExpectTextAsync(firstMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var secondServerReadyTask = voidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.SendTextMessageAsync(secondMessage, Timeouts.StepTimeoutToken);
                await servers.Server2.ExpectTextAsync(secondMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var firstServerReadyTask = voidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-1", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-1", Timeouts.StepTimeoutToken);
                await firstServerReadyTask;

                await game.SendTextMessageAsync(thirdMessage, Timeouts.StepTimeoutToken);
                await servers.Server1.ExpectTextAsync(thirdMessage, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(servers.Server1.Logs, line => line.Contains(firstMessage));
            Assert.Contains(servers.Server2.Logs, line => line.Contains(secondMessage));
            Assert.Contains(servers.Server1.Logs, line => line.Contains(thirdMessage));

            Assert.Contains(voidProxy.Logs, line => line.Contains("connected to args-server-2"));
            Assert.True(voidProxy.Logs.Count(line => line.Contains("connected to args-server-1")) is >= 2); // TODO: sometimes, proxy prints multiple times "connected to" message
        }, portableMinecraftClientFixture.Api, voidProxy, servers.Server1, servers.Server2);
    }
}
