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
    /// The servers every test family runs against: Paper for vanilla clients, whose shared servers play every protocol version, and the mod
    /// loaders the proxy supports, which run servers of the collection's own version.
    /// </summary>
    public static IReadOnlyList<ServerType> ServerTypes { get; } =
    [
        new(Name: "Paper", ModLoader: null),
        new(Name: "Forge", ModLoader: "Forge"),
        new(Name: "NeoForge", ModLoader: "NeoForge")
    ];

    public static string CollectionName(ServerType serverType, string key, bool serverPair)
    {
        return $"{serverType.Name}{ServersName(serverPair)}Collection_{key}";
    }

    public static string CollectionTypeName(ServerType serverType, string key, bool serverPair)
    {
        return $"global::{Namespace}.{CollectionName(serverType, key, serverPair)}";
    }

    /// <summary>The fixture a mod loader's collection starts its servers with, which is generated next to the collection.</summary>
    public static string ModdedFixtureName(ServerType serverType, string key, bool serverPair)
    {
        return $"{serverType.Name}{ServersName(serverPair)}Fixture_{key}";
    }

    public static string FixtureTypeName(ServerType serverType, string key, bool serverPair)
    {
        return serverType.ModLoader is null ? $"{FixturesNamespace}.PaperServersFixture" : $"global::{Namespace}.{ModdedFixtureName(serverType, key, serverPair)}";
    }

    private static string ServersName(bool serverPair)
    {
        return serverPair ? "ServerPair" : "Server";
    }
}

/// <param name="Name">The server type's name, which prefixes its collections and test classes.</param>
/// <param name="ModLoader">The mod loader the server type's servers run, by its name in the harness, or <see langword="null"/> for Paper's shared servers.</param>
public sealed record ServerType(string Name, string? ModLoader);
