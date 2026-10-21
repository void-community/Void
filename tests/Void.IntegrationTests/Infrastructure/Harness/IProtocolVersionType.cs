using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>A type that stands for one protocol version, so a fixture generic over it knows the version its test class plays.</summary>
public interface IProtocolVersionType
{
    public static abstract ProtocolVersion ProtocolVersion { get; }
}
