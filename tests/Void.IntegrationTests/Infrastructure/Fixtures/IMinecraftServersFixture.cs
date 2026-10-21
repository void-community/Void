using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Provides the servers, and the proxy in front of them, that a test class plays its protocol version on.</summary>
public interface IMinecraftServersFixture
{
    public ProtocolVersion ProtocolVersion { get; }
    public MinecraftServers Servers { get; }
    public VoidProxy VoidProxy { get; }

    /// <summary>Skips the test when there is no game for the protocol version to play.</summary>
    public void SkipIfUnavailable();
}
