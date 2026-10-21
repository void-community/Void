using Void.Minecraft.Network;

namespace Void.IntegrationTests.Generators;

/// <summary>Names the types that stand for each protocol version, from the oldest to the latest one the proxy supports.</summary>
public static class ProtocolVersionTypes
{
    public const string Namespace = "Void.IntegrationTests.ProtocolVersions";

    /// <summary>The key of every protocol version, its first release such as <c>1_7_6</c>, as in <see cref="ProtocolVersion.MINECRAFT_1_7_6"/>.</summary>
    public static IEnumerable<string> Keys => ProtocolVersion.Range(ProtocolVersion.Oldest, ProtocolVersion.Latest).Select(version => version.FirstRelease.ToString().Replace('.', '_'));

    public static string TypeName(string key)
    {
        return $"global::{Namespace}.Minecraft_{key}";
    }
}
