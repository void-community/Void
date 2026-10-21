using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Hands a collection the shared Paper servers, which play every protocol version through ViaVersion.</summary>
public sealed class PaperServersFixture(PaperFixture paperFixture) : IMinecraftServersFixture
{
    public bool IsAvailable => true;

    public IReadOnlyList<MinecraftServer> Servers { get; } = [paperFixture.Server1, paperFixture.Server2];

    public GameSetup? Setup => null;

    public void SkipIfUnavailable()
    {
    }
}
