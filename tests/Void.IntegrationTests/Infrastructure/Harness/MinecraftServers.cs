using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Harness;

public sealed record MinecraftServers(IReadOnlyList<MinecraftServer> Servers)
{
    public MinecraftServer Server1 => Servers[0];
    public MinecraftServer Server2 => Servers[1];
}
