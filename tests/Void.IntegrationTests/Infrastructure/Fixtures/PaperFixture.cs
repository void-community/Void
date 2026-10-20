using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

public class PaperFixture : IMinecraftServersFixture, IAsyncLifetime
{
    private PaperServer? _server1;
    private PaperServer? _server2;

    public PaperServer Server1 => _server1 ?? throw new InvalidOperationException($"{nameof(Server1)} is not initialized.");
    public PaperServer Server2 => _server2 ?? throw new InvalidOperationException($"{nameof(Server2)} is not initialized.");

    /// <summary>Returns both Paper servers, which accept every protocol version through ViaVersion.</summary>
    public Task<MinecraftServers> PrepareServersAsync(ProtocolVersion protocolVersion, int serverCount, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MinecraftServers([Server1, Server2], GameSetup.Vanilla(protocolVersion)));
    }

    public async ValueTask InitializeAsync()
    {
        var paperServer1 = PaperServer.CreateAsync("server-1.log", Timeouts.SetupTimeoutToken);
        var paperServer2 = PaperServer.CreateAsync("server-2.log", Timeouts.SetupTimeoutToken);

        try
        {
            await Task.WhenAll(paperServer1, paperServer2);
            _server1 = await paperServer1;
            _server2 = await paperServer2;
        }
        catch
        {
            await DisposeCreatedServersAsync(paperServer1, paperServer2);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(new[] { _server1, _server2 }.OfType<PaperServer>().Select(server => server.DisposeAsync().AsTask()));
        GC.SuppressFinalize(this);
    }

    private static async Task DisposeCreatedServersAsync(params Task<PaperServer>[] serverTasks)
    {
        await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
    }
}
