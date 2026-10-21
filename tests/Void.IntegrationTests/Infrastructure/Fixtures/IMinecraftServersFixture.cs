using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Provides the servers that the test classes of a collection share.</summary>
public interface IMinecraftServersFixture
{
    /// <summary>Whether the servers started, which they don't for a protocol version without a game to play.</summary>
    public bool IsAvailable { get; }

    public MinecraftServers Servers { get; }

    /// <summary>Skips the test when there is no game for the protocol version to play.</summary>
    public void SkipIfUnavailable();
}
