using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>The proxy in front of the two shared Paper servers, which play every protocol version through ViaVersion.</summary>
public sealed class VoidFixture<TProtocolVersion>(PaperFixture paperFixture) : MinecraftServersFixture<TProtocolVersion> where TProtocolVersion : IProtocolVersionType
{
    protected override Task<MinecraftServers?> StartServersAsync()
    {
        return Task.FromResult<MinecraftServers?>(new MinecraftServers([paperFixture.Server1, paperFixture.Server2], GameSetup.Vanilla(TProtocolVersion.ProtocolVersion)));
    }
}
