using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>Provides the servers a test runs against, which can depend on the protocol version the test runs.</summary>
public interface IMinecraftServersFixture
{
    /// <summary>Prepares at least <paramref name="serverCount"/> servers for <paramref name="protocolVersion"/>.</summary>
    public Task<MinecraftServers> PrepareServersAsync(ProtocolVersion protocolVersion, int serverCount, CancellationToken cancellationToken);
}
