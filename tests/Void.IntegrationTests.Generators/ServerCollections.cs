namespace Void.IntegrationTests.Generators;

/// <summary>
/// Names the test collections of every server type and protocol version, whose fixture holds the servers that the collection's test classes
/// share: one collection for families that play on one server, and another for families that play on a pair.
/// </summary>
public static class ServerCollections
{
    public const string Namespace = "Void.IntegrationTests.Collections";
    public const string FixturesNamespace = "global::Void.IntegrationTests.Infrastructure.Fixtures";

    /// <summary>
    /// The servers every test family runs against: Paper for vanilla clients, and the mod loaders the proxy supports. Paper's servers play every
    /// protocol version, while each mod loader runs servers of the collection's own version.
    /// </summary>
    public static IReadOnlyList<ServerType> ServerTypes { get; } =
    [
        new(Name: "Paper", HasVersionedServers: false),
        new(Name: "Forge", HasVersionedServers: true),
        new(Name: "NeoForge", HasVersionedServers: true)
    ];

    public static string CollectionName(ServerType serverType, string key, bool serverPair)
    {
        return $"{serverType.Name}{ServersName(serverPair)}Collection_{key}";
    }

    public static string CollectionTypeName(ServerType serverType, string key, bool serverPair)
    {
        return $"global::{Namespace}.{CollectionName(serverType, key, serverPair)}";
    }

    public static string FixtureTypeName(ServerType serverType, string key, bool serverPair)
    {
        var fixtureTypeName = $"{FixturesNamespace}.{serverType.Name}{ServersName(serverPair)}Fixture";

        return serverType.HasVersionedServers ? $"{fixtureTypeName}<{ProtocolVersionTypes.TypeName(key)}>" : fixtureTypeName;
    }

    private static string ServersName(bool serverPair)
    {
        return serverPair ? "ServerPair" : "Server";
    }
}

/// <param name="Name">The server type's name, which prefixes its fixtures, collections and test classes.</param>
/// <param name="HasVersionedServers">Whether the server type's fixtures are generic over the protocol version their servers run.</param>
public sealed record ServerType(string Name, bool HasVersionedServers);
