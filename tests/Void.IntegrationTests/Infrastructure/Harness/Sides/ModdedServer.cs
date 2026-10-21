using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Modding;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

/// <summary>A Forge or NeoForge server, whose loader build and mods the image picks for its Minecraft release, which clients then copy.</summary>
public record ModdedServer(IContainer Container, string LogFileName, ModLoader ModLoader, ReleaseVersion MinecraftRelease) : MinecraftServer(Container, LogFileName)
{
    private const string ModsDirectory = "/data/mods/";
    private static readonly ReleaseVersion LargerMemoryRelease = new(Major: 1, Minor: 17, Patch: 0);

    /// <param name="contentModProject">The Modrinth project of a mod that must be installed on both sides, which the image installs with the mods it requires.</param>
    public static async Task<ModdedServer> CreateAsync(ModLoader loader, ReleaseVersion release, string contentModProject, string logFileName, CancellationToken cancellationToken = default)
    {
        // The image has no Java 16 variant, and Java 17 runs those releases too.
        var javaMajorVersion = await MojangVersionCatalog.FindJavaMajorVersionAsync(release, cancellationToken);
        javaMajorVersion = javaMajorVersion is 16 ? 17 : javaMajorVersion;

        var builder = new ContainerBuilder($"itzg/minecraft-server:java{javaMajorVersion}")
            .WithImagePullPolicy(PullPolicy.Missing)
            .WithPortBinding(port: 25565, assignRandomHostPort: true)
            .WithEnvironment("EULA", "TRUE")
            .WithEnvironment("TYPE", loader is ModLoader.Forge ? "FORGE" : "NEOFORGE")
            .WithEnvironment("VERSION", release.ToString())
            .WithEnvironment("MODRINTH_PROJECTS", contentModProject)
            .WithEnvironment("MODRINTH_DOWNLOAD_DEPENDENCIES", "required")
            .WithEnvironment("MEMORY", release.CompareTo(LargerMemoryRelease) < 0 ? "1536M" : "2G")
            .WithEnvironment("DIFFICULTY", "peaceful")
            .WithEnvironment("MODE", "creative")
            .WithEnvironment("GENERATE_STRUCTURES", "FALSE")
            .WithEnvironment("SPAWN_ANIMALS", "FALSE")
            .WithEnvironment("SPAWN_MONSTERS", "FALSE")
            .WithEnvironment("SPAWN_NPCS", "FALSE")
            .WithEnvironment("VIEW_DISTANCE", "2")
            .WithEnvironment("SIMULATION_DISTANCE", "2")
            .WithEnvironment("ONLINE_MODE", "FALSE")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("For help, type \"help\"", options => options.WithTimeout(TimeSpan.FromMinutes(10))));

        return new ModdedServer(await StartContainerAsync(builder, $"{loader} server for Minecraft {release} with {contentModProject}", cancellationToken), logFileName, loader, release);
    }

    /// <summary>Reads the loader build and mods the image installed, which clients start to play on the server.</summary>
    public async Task<GameSetup> ReadSetupAsync(CancellationToken cancellationToken = default)
    {
        // The image records the loader build it installed, by the version servers report it as.
        var manifestPath = ModLoader is ModLoader.Forge ? "/data/.forge-manifest.json" : "/data/.neoforge-manifest.json";
        using var manifest = JsonDocument.Parse(await Container.ReadFileAsync(manifestPath, cancellationToken));
        var loaderVersion = manifest.RootElement.GetProperty("forgeVersion").GetString() ?? throw new IntegrationTestException($"{manifestPath} names no {ModLoader} build");

        var listing = await Container.ExecAsync(["find", ModsDirectory, "-maxdepth", "1", "-name", "*.jar"], cancellationToken);
        var mods = new List<ModFile>();

        foreach (var modPath in listing.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal))
            mods.Add(new ModFile(Path.GetFileName(modPath), await Container.ReadFileAsync(modPath, cancellationToken)));

        return new GameSetup(MinecraftRelease, new LoaderBuild(ModLoader, loaderVersion), mods);
    }
}
