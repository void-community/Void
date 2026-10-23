using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

/// <summary>A Forge or NeoForge server, whose loader build and mods the image picks for its Minecraft release, which clients then copy.</summary>
public record ModdedServer(IContainer Container, string LogFileName, ModLoader ModLoader, ReleaseVersion MinecraftRelease) : MinecraftServer(Container, LogFileName)
{
    private const string ModsDirectory = "/data/mods/";
    private const string DebugLogPath = "/data/logs/debug.log";
    private const string DebugLoggingPath = "/opt/void/log4j2-debug.xml";

    // Forge's console keeps its usual lines, without the formatting codes servers before Minecraft 1.13 put in them, and the steps of its login
    // handshake, which show where a failed login stopped, while its debug log gets everything Forge and Minecraft log at debug level, every packet
    // included. It uses only the parts of log4j every Forge ships.
    private const string DebugLogging = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Configuration status="warn">
            <Appenders>
                <Console name="Console" target="SYSTEM_OUT">
                    <Filters>
                        <MarkerFilter marker="FMLHANDSHAKE" onMatch="ACCEPT" onMismatch="NEUTRAL"/>
                        <ThresholdFilter level="info" onMatch="ACCEPT" onMismatch="DENY"/>
                    </Filters>
                    <PatternLayout pattern="[%d{HH:mm:ss}] [%t/%level] [%logger{1}]: %replace{%msg}{§[0-9a-fk-orA-FK-OR]}{}%n%xEx"/>
                </Console>
                <RollingRandomAccessFile name="DebugFile" fileName="logs/debug.log" filePattern="logs/debug-%i.log.gz">
                    <PatternLayout pattern="[%d{HH:mm:ss.SSS}] [%t/%level] [%logger]: %msg%n%xEx"/>
                    <Policies>
                        <OnStartupTriggeringPolicy/>
                        <SizeBasedTriggeringPolicy size="200MB"/>
                    </Policies>
                    <DefaultRolloverStrategy max="5" fileIndex="min"/>
                </RollingRandomAccessFile>
            </Appenders>
            <Loggers>
                <Root level="debug">
                    <AppenderRef ref="Console"/>
                    <AppenderRef ref="DebugFile"/>
                </Root>
            </Loggers>
        </Configuration>
        """;

    // Later builds resolve NeoForge servers' mods as Forge ones (itzg/docker-minecraft-server@625b10ae), which NeoForge from 1.20.2 can't load.
    private const string ImageRelease = "2026.10.0";

    private static readonly ReleaseVersion LargerMemoryRelease = new(Major: 1, Minor: 17, Patch: 0);

    /// <param name="loaderVersion">The loader build as the image selects it, such as beta for NeoForge's newest beta, or <see langword="null"/> for its newest stable build.</param>
    /// <param name="contentMod">A mod that must be installed on both sides, as the image takes Modrinth projects, such as storagedrawers, or refined-storage:alpha for its newest build of any kind. The image installs it with the mods it requires.</param>
    public static async Task<ModdedServer> CreateAsync(ModLoader loader, ReleaseVersion release, string? loaderVersion, string contentMod, string logFileName, CancellationToken cancellationToken = default)
    {
        // The image has no Java 16 variant, and Java 17 runs those releases too.
        var javaMajorVersion = await MojangVersionCatalog.FindJavaMajorVersionAsync(release, cancellationToken);
        javaMajorVersion = javaMajorVersion is 16 ? 17 : javaMajorVersion;

        var builder = new ContainerBuilder($"itzg/minecraft-server:{ImageRelease}-java{javaMajorVersion}")
            .WithImagePullPolicy(PullPolicy.Missing)
            .WithPortBinding(port: 25565, assignRandomHostPort: true)
            .WithEnvironment("EULA", "TRUE")
            .WithEnvironment("TYPE", loader is ModLoader.Forge ? "FORGE" : "NEOFORGE")
            .WithEnvironment("VERSION", release.ToString())
            .WithEnvironment("MODRINTH_PROJECTS", contentMod)
            .WithEnvironment("MODRINTH_DOWNLOAD_DEPENDENCIES", "required")
            .WithEnvironment("MEMORY", release.CompareTo(LargerMemoryRelease) < 0 ? "1536M" : "2G")
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
            // Tests run commands on the console through a pipe
            .WithEnvironment("CREATE_CONSOLE_IN_PIPE", "TRUE")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("For help, type \"help\"", options => options.WithTimeout(TimeSpan.FromMinutes(10))));

        if (loaderVersion is not null)
            builder = builder.WithEnvironment(loader is ModLoader.Forge ? "FORGE_VERSION" : "NEOFORGE_VERSION", loaderVersion);

        string jvmOptions;

        if (loader is ModLoader.Forge)
        {
            builder = builder.WithResourceMapping(Encoding.UTF8.GetBytes(DebugLogging), DebugLoggingPath);
            jvmOptions = $"-Dlog4j.configurationFile={DebugLoggingPath}";
        }
        else
        {
            // NeoForge sets its logging up again while it starts, whose debug log also takes Minecraft's debug lines and packets once let through
            jvmOptions = "-Dforge.logging.mojang.level=debug -Dforge.logging.marker.networking=ACCEPT";
        }

        builder = builder
            .WithResourceMapping(ModLoaderFixes.Url, ModLoaderFixes.ServerPath)
            .WithEnvironment("JVM_OPTS", $"-javaagent:{ModLoaderFixes.ServerPath} {jvmOptions}");

        return new ModdedServer(await StartContainerAsync(builder, $"{loader} {loaderVersion ?? "stable"} server for Minecraft {release} with {contentMod}", cancellationToken), logFileName, loader, release);
    }

    /// <summary>Saves the server's debug log, with everything Forge and Minecraft logged at debug level and every packet, into a directory.</summary>
    public async Task SaveDebugLogAsync(string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(LogFileName)}-debug.log"), await Container.ReadFileAsync(DebugLogPath, cancellationToken), cancellationToken);
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

        return new GameSetup(ModLoader, loaderVersion, MinecraftRelease, mods);
    }
}
