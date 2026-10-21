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

public abstract class ProxiedServerRedirectionTestBase(ProtocolVersion protocolVersion, IMinecraftServersFixture serversFixture, IVoidFixture voidFixture, PortableMinecraftClientFixture portableMinecraftClientFixture) : IntegrationUnitBase, IClassFixture<PortableMinecraftClientFixture>
{
    private EndPoint ProxyEndPoint => new IPEndPoint(IPAddress.Loopback, voidFixture.VoidProxy.Port);

    protected async Task RunAsync()
    {
        if (!portableMinecraftClientFixture.Api.SupportedVersions.Contains(protocolVersion))
            Assert.Skip($"Protocol version {protocolVersion} is not supported by the client, skipping test.");

        serversFixture.SkipIfUnavailable();

        var firstMessage = $"server1-{Guid.NewGuid()}";
        var secondMessage = $"server2-{Guid.NewGuid()}";
        var thirdMessage = $"server1-{Guid.NewGuid()}";

        await LoggedExecutorAsync(async () =>
        {
            Task playerDisconnectionTask;

            await using (var game = await portableMinecraftClientFixture.Api.RunGameAsync(nameof(ProxiedServerRedirectionTestBase), protocolVersion, serversFixture.Setup, [voidFixture.VoidProxy, serversFixture.Server1, serversFixture.Server2], Timeouts.SetupTimeoutToken))
            {
                playerDisconnectionTask = voidFixture.VoidProxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

                await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
                await serversFixture.Server1.Container.ExpectTextAsync($"{game.Username} joined the game", game.StartedAt, Timeouts.SetupTimeoutToken);
                await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

                await game.SendTextMessageAsync(firstMessage, Timeouts.StepTimeoutToken);
                await serversFixture.Server1.ExpectTextAsync(firstMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var secondServerReadyTask = voidFixture.VoidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-2", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-2", Timeouts.StepTimeoutToken);
                await secondServerReadyTask;

                await game.SendTextMessageAsync(secondMessage, Timeouts.StepTimeoutToken);
                await serversFixture.Server2.ExpectTextAsync(secondMessage, lookupHistory: true, Timeouts.StepTimeoutToken);

                var firstServerReadyTask = voidFixture.VoidProxy.WaitForServerConnectionAndKeepAliveAsync(game.Username, "args-server-1", Timeouts.StepTimeoutToken);
                await game.SendTextMessageAsync("/server args-server-1", Timeouts.StepTimeoutToken);
                await firstServerReadyTask;

                await game.SendTextMessageAsync(thirdMessage, Timeouts.StepTimeoutToken);
                await serversFixture.Server1.ExpectTextAsync(thirdMessage, lookupHistory: true, Timeouts.StepTimeoutToken);
            }

            await playerDisconnectionTask;

            Assert.Contains(serversFixture.Server1.Logs, line => line.Contains(firstMessage));
            Assert.Contains(serversFixture.Server2.Logs, line => line.Contains(secondMessage));
            Assert.Contains(serversFixture.Server1.Logs, line => line.Contains(thirdMessage));

            Assert.Contains(voidFixture.VoidProxy.Logs, line => line.Contains("connected to args-server-2"));
            Assert.True(voidFixture.VoidProxy.Logs.Count(line => line.Contains("connected to args-server-1")) is >= 2); // TODO: sometimes, proxy prints multiple times "connected to" message
        }, portableMinecraftClientFixture.Api, voidFixture.VoidProxy, serversFixture.Server1, serversFixture.Server2);
    }
}
