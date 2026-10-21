using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Provides the servers that the test classes of a collection share.</summary>
public interface IMinecraftServersFixture
{
    /// <summary>Whether the servers started, which they don't for a protocol version without a game to play.</summary>
    public bool IsAvailable { get; }

    public IReadOnlyList<MinecraftServer> Servers { get; }

    public MinecraftServer Server1 => Servers[0];

    public MinecraftServer Server2 => Servers[1];

    /// <summary>The modded game the servers run, which clients start to play on them, or <see langword="null"/> when clients play vanilla Minecraft of their own protocol version.</summary>
    public GameSetup? Setup { get; }

    /// <summary>Skips the test when there is no game for the protocol version to play.</summary>
    public void SkipIfUnavailable();
}
