using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Plays a protocol version on the shared Paper servers, which play every protocol version through ViaVersion.</summary>
public abstract class PaperConnectionsFixture(PaperFixture paperFixture, ProtocolVersion protocolVersion) : ConnectionsFixture(protocolVersion)
{
    private MinecraftServer[]? _servers;

    public override bool IsAvailable => _servers is not null;

    public override IReadOnlyList<MinecraftServer> Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");

    public override GameSetup? Setup => null;

    protected override async Task StartServersAsync()
    {
        _servers = [.. await paperFixture.GetServersAsync()];
    }

    // Every Paper collection plays on the same servers, which the assembly fixture stops.
    protected override Task DisposeServersAsync()
    {
        return Task.CompletedTask;
    }
}
