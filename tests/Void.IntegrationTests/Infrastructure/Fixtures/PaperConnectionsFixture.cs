using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Plays a protocol version on Paper servers, which play every protocol version through ViaVersion.</summary>
public abstract class PaperConnectionsFixture(ProtocolVersion protocolVersion) : ConnectionsFixture(protocolVersion)
{
    private MinecraftServer[]? _servers;

    public override bool IsAvailable => _servers is not null;

    public override IReadOnlyList<MinecraftServer> Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");

    public override GameSetup? Setup => null;

    protected override async Task StartServersAsync()
    {
        Task<PaperServer>[] serverTasks = [PaperServer.CreateAsync("server-1.log", Timeouts.SetupTimeoutToken), PaperServer.CreateAsync("server-2.log", Timeouts.SetupTimeoutToken)];

        try
        {
            _servers = await Task.WhenAll(serverTasks);
        }
        catch
        {
            await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
            throw;
        }
    }

    protected override async Task DisposeServersAsync()
    {
        if (_servers is { } servers)
            await Task.WhenAll(servers.Select(server => server.DisposeAsync().AsTask()));
    }
}
