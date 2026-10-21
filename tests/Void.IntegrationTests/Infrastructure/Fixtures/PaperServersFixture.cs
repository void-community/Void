using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Hands a collection the shared Paper servers, which play every protocol version through ViaVersion.</summary>
public abstract class PaperServersFixture(IReadOnlyList<MinecraftServer> servers) : IMinecraftServersFixture
{
    public bool IsAvailable => true;

    // Clients play vanilla Minecraft of their own protocol version, which needs no setup of the servers.
    public MinecraftServers Servers { get; } = new(servers, Setup: null);

    public void SkipIfUnavailable()
    {
    }
}
