using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>Servers that run the same game, which clients start from <see cref="Setup"/> to play on them.</summary>
public sealed record MinecraftServers(IReadOnlyList<MinecraftServer> Servers, GameSetup Setup)
{
    public MinecraftServer Server1 => Servers[0];
    public MinecraftServer Server2 => Servers[1];
}
