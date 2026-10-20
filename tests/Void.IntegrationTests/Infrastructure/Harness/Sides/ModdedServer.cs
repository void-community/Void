using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Void.IntegrationTests.Infrastructure.Modding;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

/// <summary>A Forge or NeoForge server with the same loader build and mods as the clients that play on it.</summary>
public record ModdedServer(IContainer Container, string LogFileName) : MinecraftServer(Container, LogFileName)
{
    private static readonly ReleaseVersion LargerMemoryRelease = new(Major: 1, Minor: 17, Patch: 0);

    public static async Task<ModdedServer> CreateAsync(GameSetup setup, string logFileName, CancellationToken cancellationToken = default)
    {
        var loader = setup.Loader ?? throw new ArgumentException($"{setup} has no mod loader for a modded server", nameof(setup));
        var forge = loader.ModLoader is ModLoader.Forge;

        // The image has no Java 16 variant, and Java 17 runs those releases too.
        var javaMajorVersion = await MojangJavaVersionCatalog.FindJavaMajorVersionAsync(setup.MinecraftRelease, cancellationToken);
        javaMajorVersion = javaMajorVersion is 16 ? 17 : javaMajorVersion;

        // Forge servers run the build's own installer, NeoForge servers install their build by version.
        var (loaderSetting, loaderValue) = loader.ServerInstallerUri is { } installerUri
            ? ("FORGE_INSTALLER_URL", installerUri.AbsoluteUri)
            : (forge ? "FORGE_VERSION" : "NEOFORGE_VERSION", loader.ServerLoaderVersion);
        var builder = new ContainerBuilder($"itzg/minecraft-server:java{javaMajorVersion}")
            .WithImagePullPolicy(PullPolicy.Missing)
            .WithPortBinding(port: 25565, assignRandomHostPort: true)
            .WithEnvironment("EULA", "TRUE")
            .WithEnvironment("TYPE", forge ? "FORGE" : "NEOFORGE")
            .WithEnvironment("VERSION", setup.MinecraftRelease.ToString())
            .WithEnvironment(loaderSetting, loaderValue)
            .WithEnvironment("MEMORY", setup.MinecraftRelease.CompareTo(LargerMemoryRelease) < 0 ? "1536M" : "2G")
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

        // The image copies everything in /mods into the server's mods folder.
        foreach (var mod in setup.Mods)
            builder = builder.WithResourceMapping(new FileInfo(mod.LocalPath), "/mods/");

        return new ModdedServer(await StartContainerAsync(builder, $"{setup} server", cancellationToken), logFileName);
    }
}
