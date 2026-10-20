using System;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

public record PaperServer(IContainer Container, string LogFileName) : MinecraftServer(Container, LogFileName)
{
    public static async Task<PaperServer> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await CreateAsync("server.log", cancellationToken);
    }

    public static async Task<PaperServer> CreateAsync(string logFileName, CancellationToken cancellationToken = default, int? maximumPlayers = null)
    {
        var builder = new ContainerBuilder("itzg/minecraft-server:latest")
            .WithImagePullPolicy(PullPolicy.Always)
            .WithPortBinding(port: 25565, assignRandomHostPort: true)
            .WithEnvironment("EULA", "TRUE")
            .WithEnvironment("TYPE", "PAPER")
            .WithEnvironment("VERSION", "1.21.4")
            .WithEnvironment("DIFFICULTY", "peaceful")
            .WithEnvironment("MODE", "creative")
            .WithEnvironment("GENERATE_STRUCTURES", "FALSE")
            .WithEnvironment("SPAWN_ANIMALS", "FALSE")
            .WithEnvironment("SPAWN_MONSTERS", "FALSE")
            .WithEnvironment("SPAWN_NPCS", "FALSE")
            .WithEnvironment("VIEW_DISTANCE", "2")
            .WithEnvironment("SIMULATION_DISTANCE", "2")
            .WithEnvironment("ONLINE_MODE", "FALSE")
            .WithEnvironment("MODRINTH_PROJECTS", "viaversion,viabackwards,viarewind")
            .WithEnvironment("JVM_OPTS", "-Dpaper.playerconnection.keepalive=120")
            .WithEnvironment("PATCH_DEFINITIONS", "/patches")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("For help, type \"help\"", options => options.WithTimeout(TimeSpan.FromMinutes(5))))
            .WithResourceMapping([
                    ..
                    """
                    {
                      "file": "/data/bukkit.yml",
                      "ops": [
                        {
                          "$set": {
                            "path": "$.settings['connection-throttle']",
                            "value": -1,
                            "value-type": "int"
                          }
                        }
                      ]
                    }
                    """u8
                ],
                FilePath.Of("/patches/connection-throttle.json"));

        if (maximumPlayers is { } value)
            builder = builder.WithEnvironment("MAX_PLAYERS", value.ToString());

        return new PaperServer(await StartContainerAsync(builder, "Paper server", cancellationToken), logFileName);
    }
}
