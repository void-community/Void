using System.Text;
using Microsoft.CodeAnalysis;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Generators.Sources;

/// <summary>
/// Generates a test class of every connection test family for every server type and protocol version, from the oldest to the latest one the
/// proxy supports, such as <c>PaperDirectConnectionTests_1_12_1</c> and <c>ForgeDirectConnectionTests_1_12_1</c>. Each class runs in a test
/// collection of its server type and protocol version, whose fixture holds the servers, proxy and games the collection's classes share.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConnectionTestsGenerator : IIncrementalGenerator
{
    private const string CollectionsNamespace = "Void.IntegrationTests.Collections";
    private const string FixturesNamespace = "global::Void.IntegrationTests.Infrastructure.Fixtures";

    /// <summary>
    /// The servers every family runs against: Paper for vanilla clients, or vanilla Minecraft's server for a version Paper never built, and the
    /// mod loaders the proxy supports. Each collection starts its own servers of its own version.
    /// </summary>
    private static readonly ServerType[] ServerTypes =
    [
        new(Name: "Paper", IsModded: false),
        new(Name: "Forge", IsModded: true),
        new(Name: "NeoForge", IsModded: true)
    ];

    private static readonly Family[] Families =
    [
        new(
            Name: "DirectConnectionTests",
            BaseClassName: "DirectConnectionTestBase",
            MethodNameFormat: "PortableMinecraftClientConnectsTo{0}Server"
        ),
        new(
            Name: "ProxiedConnectionTests",
            BaseClassName: "ProxiedConnectionTestBase",
            MethodNameFormat: "PortableMinecraftClientConnectsTo{0}ServerThroughProxy"
        ),
        new(
            Name: "ProxiedPlayerTests",
            BaseClassName: "global::Void.IntegrationTests.Connections.ProxiedPlayerTestBase",
            MethodNameFormat: "PortableMinecraftClientsExposeReciprocalCoordinates"
        ),
        new(
            Name: "ProxiedServerRedirectionTests",
            BaseClassName: "ProxiedServerRedirectionTestBase",
            MethodNameFormat: "PortableMinecraftClientMovesBetween{0}ServersThroughProxy"
        ),
        new(
            Name: "ProxiedKickTests",
            BaseClassName: "ProxiedKickTestBase",
            MethodNameFormat: "PortableMinecraftClientShows{0}ServerKickReasonsThroughProxy"
        ),
        new(
            Name: "EssentialsCommandsTests",
            BaseClassName: "EssentialsCommandsTestBase",
            MethodNameFormat: "PortableMinecraftClientUsesEssentialsCommandsOn{0}ServersThroughProxy"
        ),
        new(
            Name: "EssentialsStopTests",
            BaseClassName: "EssentialsStopTestBase",
            MethodNameFormat: "PortableMinecraftClientStopsProxyOf{0}ServersWithEssentials"
        ),
        new(
            Name: "ReloadedPluginsTests",
            BaseClassName: "ReloadedPluginsTestBase",
            MethodNameFormat: "PortableMinecraftClientPlaysOn{0}ServersAfterEveryPluginReloads"
        ),
        new(
            Name: "UnloadedPluginsTests",
            BaseClassName: "UnloadedPluginsTestBase",
            MethodNameFormat: "PortableMinecraftClientPlaysOn{0}ServerAfterAllPluginsUnloadAndLoad"
        ),
        new(
            Name: "RepeatedlyReloadedPluginsTests",
            BaseClassName: "RepeatedlyReloadedPluginsTestBase",
            MethodNameFormat: "PluginReloadsBehind{0}ServersLeaveNoContextsOrMemory"
        ),
        new(
            Name: "ReorderedPluginsTests",
            BaseClassName: "ReorderedPluginsTestBase",
            MethodNameFormat: "PortableMinecraftClientPlaysOn{0}ServerAfterPluginsReloadInAnyOrder"
        ),
        new(
            Name: "ProxiedPluginReloadTests",
            BaseClassName: "ProxiedPluginReloadTestBase",
            MethodNameFormat: "PortableMinecraftClientKeepsPlayingOn{0}ServersWhilePluginsReload"
        ),
        new(
            Name: "ProxiedProtocolUnloadTests",
            BaseClassName: "ProxiedProtocolUnloadTestBase",
            MethodNameFormat: "PortableMinecraftClientIsKickedFrom{0}ServerWhenItsProtocolUnloads"
        ),
        new(
            Name: "ProxiedLoginPluginReloadTests",
            BaseClassName: "ProxiedLoginPluginReloadTestBase",
            MethodNameFormat: "PortableMinecraftClientJoins{0}ServerWhilePluginsReload"
        ),
        new(
            Name: "ProxiedRedirectionPluginReloadTests",
            BaseClassName: "ProxiedRedirectionPluginReloadTestBase",
            MethodNameFormat: "PortableMinecraftClientMovesBetween{0}ServersWhilePluginsReload"
        ),
        new(
            Name: "ProxiedPluginsCommandTests",
            BaseClassName: "ProxiedPluginsCommandTestBase",
            MethodNameFormat: "PortableMinecraftClientReloadsEssentialsOn{0}ServersWithPluginsCommand"
        ),
        new(
            Name: "PluginLeftoversTests",
            BaseClassName: "PluginLeftoversTestBase",
            MethodNameFormat: "PluginReloadsBehind{0}ServersLeaveNothingBehind"
        ),
        new(
            Name: "PluginContainerConflictsTests",
            BaseClassName: "PluginContainerConflictsTestBase",
            MethodNameFormat: "PluginContainersBehind{0}ServersRefuseConflictingOperations"
        ),
        new(
            Name: "ServerStatusPluginReloadTests",
            BaseClassName: "ServerStatusPluginReloadTestBase",
            MethodNameFormat: "ProxyAnswers{0}ServerStatusWhilePluginsReload"
        ),
        new(
            Name: "ProxyRestartPluginReloadTests",
            BaseClassName: "ProxyRestartPluginReloadTestBase",
            MethodNameFormat: "ProxyRestartedAfterPluginReloadsPlaysOn{0}Servers"
        )
    ];

    /// <summary>The key of every protocol version, its first release such as <c>1_7_6</c>, as in <see cref="ProtocolVersion.MINECRAFT_1_7_6"/>.</summary>
    private static IEnumerable<string> ProtocolVersionKeys => ProtocolVersion.Range(ProtocolVersion.Oldest, ProtocolVersion.Latest).Select(version => version.FirstRelease.ToString().Replace('.', '_'));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var collectionsSourceProvider = context.CompilationProvider.Select(static (_, cancellationToken) => GenerateCollectionsSource(cancellationToken));
        context.RegisterSourceOutput(collectionsSourceProvider, static (sourceProductionContext, generatedSource) => sourceProductionContext.AddSource("ServerCollections.g.cs", generatedSource));

        foreach (var family in Families)
        {
            var familySourceProvider = context.CompilationProvider.Select((_, cancellationToken) => GenerateFamilySource(family, cancellationToken));
            context.RegisterSourceOutput(familySourceProvider, (sourceProductionContext, generatedSource) => sourceProductionContext.AddSource($"{family.Name}.g.cs", generatedSource));
        }
    }

    private static string GenerateCollectionsSource(CancellationToken cancellationToken)
    {
        var sourceBuilder = new StringBuilder();

        sourceBuilder.AppendLine("// <auto-generated />");
        sourceBuilder.AppendLine("#nullable enable");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine($"namespace {CollectionsNamespace};");
        sourceBuilder.AppendLine();

        foreach (var serverType in ServerTypes)
        {
            foreach (var key in ProtocolVersionKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var protocolVersion = $"global::Void.Minecraft.Network.ProtocolVersion.MINECRAFT_{key}";

                sourceBuilder.AppendLine(serverType.IsModded
                    ? $"public sealed class {FixtureName(serverType, key)}() : {FixturesNamespace}.ModdedConnectionsFixture(global::Void.IntegrationTests.Infrastructure.Harness.ModLoader.{serverType.Name}, {protocolVersion});"
                    : $"public sealed class {FixtureName(serverType, key)}() : {FixturesNamespace}.PaperConnectionsFixture({protocolVersion});");
                sourceBuilder.AppendLine();
                sourceBuilder.AppendLine("[global::Xunit.CollectionDefinition]");
                sourceBuilder.AppendLine($"public sealed class {CollectionName(serverType, key)} : global::Xunit.ICollectionFixture<{FixtureName(serverType, key)}>;");
                sourceBuilder.AppendLine();
            }
        }

        return sourceBuilder.ToString();
    }

    private static string GenerateFamilySource(Family family, CancellationToken cancellationToken)
    {
        var sourceBuilder = new StringBuilder();

        sourceBuilder.AppendLine("// <auto-generated />");
        sourceBuilder.AppendLine("#nullable enable");
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine($"namespace Void.IntegrationTests.Connections.{family.Name};");
        sourceBuilder.AppendLine();

        foreach (var serverType in ServerTypes)
        {
            var methodName = string.Format(family.MethodNameFormat, serverType.Name);

            foreach (var key in ProtocolVersionKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var className = $"{serverType.Name}{family.Name}_{key}";

                sourceBuilder.AppendLine($"[global::Xunit.Collection(typeof(global::{CollectionsNamespace}.{CollectionName(serverType, key)}))]");
                sourceBuilder.AppendLine($"public sealed class {className}(global::{CollectionsNamespace}.{FixtureName(serverType, key)} fixture) : {family.BaseClassName}(fixture)");
                sourceBuilder.AppendLine("{");
                sourceBuilder.AppendLine("    [global::Xunit.Fact]");
                sourceBuilder.AppendLine($"    public async global::System.Threading.Tasks.Task {methodName}()");
                sourceBuilder.AppendLine("    {");
                sourceBuilder.AppendLine("        await base.RunAsync();");
                sourceBuilder.AppendLine("    }");
                sourceBuilder.AppendLine("}");
                sourceBuilder.AppendLine();
            }
        }

        return sourceBuilder.ToString();
    }

    private static string CollectionName(ServerType serverType, string key)
    {
        return $"{serverType.Name}ConnectionsCollection_{key}";
    }

    private static string FixtureName(ServerType serverType, string key)
    {
        return $"{serverType.Name}ConnectionsFixture_{key}";
    }

    /// <param name="Name">The server type's name, which prefixes its collections and test classes, and names its mod loader when it's modded.</param>
    private sealed record ServerType(string Name, bool IsModded);

    /// <param name="Name">The family's class name without its server type prefix and protocol version suffix.</param>
    /// <param name="BaseClassName">The test base the family's classes derive from, which takes the collection's fixture.</param>
    /// <param name="MethodNameFormat">The test method name, with <c>{0}</c> standing for the server type.</param>
    private sealed record Family(string Name, string BaseClassName, string MethodNameFormat);
}
