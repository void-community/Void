using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

/// <summary>A server that vanilla clients play on: Paper's for a release Paper built, or vanilla Minecraft's for one Paper didn't.</summary>
public record PaperServer(IContainer Container, string LogFileName) : MinecraftServer(Container, LogFileName)
{
    /// <summary>What the image reports, before the server starts, for a release that Paper has no build for.</summary>
    public const string MissingBuildMessage = "Invalid parameter provided for 'install-paper' command";

    /// <summary>What Paper's early builds for Minecraft 1.12 report when they start, since they download Mojang's server from an address Mojang no longer serves.</summary>
    public const string UnusableBuildMessage = "Error downloading original jar";

    /// <param name="vanilla">Whether to start vanilla Minecraft's server rather than Paper's.</param>
    public static async Task<PaperServer> CreateAsync(ReleaseVersion release, bool vanilla, string logFileName, CancellationToken cancellationToken = default, int? maximumPlayers = null)
    {
        // The image has no Java 16 variant, and Java 17 runs those releases too.
        var javaMajorVersion = await MojangVersionCatalog.FindJavaMajorVersionAsync(release, cancellationToken);
        javaMajorVersion = javaMajorVersion is 16 ? 17 : javaMajorVersion;

        var builder = new ContainerBuilder($"itzg/minecraft-server:java{javaMajorVersion}")
            .WithImagePullPolicy(PullPolicy.Always)
            .WithPortBinding(port: 25565, assignRandomHostPort: true)
            .WithEnvironment("EULA", "TRUE")
            .WithEnvironment("TYPE", vanilla ? "VANILLA" : "PAPER")
            .WithEnvironment("VERSION", release.ToString())
            .WithEnvironment("DIFFICULTY", "peaceful")
            .WithEnvironment("MODE", "creative")
            .WithEnvironment("GENERATE_STRUCTURES", "FALSE")
            .WithEnvironment("SPAWN_ANIMALS", "FALSE")
            .WithEnvironment("SPAWN_MONSTERS", "FALSE")
            .WithEnvironment("SPAWN_NPCS", "FALSE")
            // New players spawn up to 10 blocks from the spawn point each way, and servers before 1.14 show players only within one chunk less
            // than their view distance, so two players see each other wherever they spawn only from a view distance of 3.
            .WithEnvironment("VIEW_DISTANCE", "3")
            .WithEnvironment("SIMULATION_DISTANCE", "2")
            .WithEnvironment("ONLINE_MODE", "FALSE")
            // Paper before Minecraft 1.17 patches Mojang's server on its first start, which takes minutes when servers start side by side.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("For help, type \"help\"", options => options.WithTimeout(TimeSpan.FromMinutes(10))));

        // Paper's own settings: a longer keep-alive, and no throttle on players that reconnect at once, which vanilla servers don't have.
        if (!vanilla)
        {
            // Paper before Minecraft 1.14 reads commands from a console the container doesn't attach, and spins on its empty input otherwise.
            builder = builder
                .WithEnvironment("CONSOLE", "FALSE")
                // Paper for Minecraft 1.17 refuses any Java but 16, which the image has no variant for, while it runs on Java 17 too.
                .WithEnvironment("JVM_OPTS", "-Dpaper.playerconnection.keepalive=120 -DPaper.IgnoreJavaVersion=true")
                .WithEnvironment("PATCH_DEFINITIONS", "patches")
                // The image starts from default configs, which it has no bukkit.yml for on some releases, such as 1.7.10 and 1.18.1. There an
                // empty one takes its place, so the throttle can still be patched.
                .WithEnvironment("DOWNLOAD_EXTRA_CONFIGS", "patches<https://raw.githubusercontent.com/caunt/ItzgTemplates/refs/heads/main/bukkit-yml.patch.json,"
                    + "https://raw.githubusercontent.com/caunt/ItzgTemplates/refs/heads/main/bukkit.yml")
                .WithEnvironment("CFG_BUKKIT_CONNECTION_THROTTLE", "-1");
        }

        if (maximumPlayers is { } value)
            builder = builder.WithEnvironment("MAX_PLAYERS", value.ToString());

        return new PaperServer(await StartContainerAsync(builder, $"{(vanilla ? "Vanilla" : "Paper")} server for Minecraft {release}", cancellationToken), logFileName);
    }
}
