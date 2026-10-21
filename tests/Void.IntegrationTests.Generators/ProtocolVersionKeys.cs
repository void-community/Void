using Void.Minecraft.Network;

namespace Void.IntegrationTests.Generators;

/// <summary>Names every protocol version from the oldest to the latest one the proxy supports, for the generated types of each.</summary>
public static class ProtocolVersionKeys
{
    /// <summary>The key of every protocol version, its first release such as <c>1_7_6</c>, as in <see cref="ProtocolVersion.MINECRAFT_1_7_6"/>.</summary>
    public static IEnumerable<string> All => ProtocolVersion.Range(ProtocolVersion.Oldest, ProtocolVersion.Latest).Select(version => version.FirstRelease.ToString().Replace('.', '_'));
}
