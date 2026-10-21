using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>A type that stands for one protocol version, so a servers fixture generic over it knows the version its collection plays.</summary>
public interface IProtocolVersionType
{
    public static abstract ProtocolVersion ProtocolVersion { get; }
}
