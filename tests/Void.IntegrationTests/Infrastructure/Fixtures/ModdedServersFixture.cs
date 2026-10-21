using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.IntegrationTests.Infrastructure.Modding;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// Runs servers of a mod loader for one protocol version, since modded clients only join servers of their own version and mods. A protocol
/// version the loader or mods haven't released for starts nothing, and its tests skip.
/// </summary>
public abstract class ModdedServersFixture<TProtocolVersion>(ModLoader loader, int serverCount) : MinecraftServersFixture<TProtocolVersion> where TProtocolVersion : IProtocolVersionType
{
    private ModdedGameResolution? _unavailable;
    private MinecraftServer[] _servers = [];

    public override void SkipIfUnavailable()
    {
        if (_unavailable is { } resolution)
            SkippedTestWarnings.Skip(resolution.SkipReason, resolution.WarnOnSkip);
    }

    protected override async Task<MinecraftServers?> StartServersAsync()
    {
        var protocolVersion = TProtocolVersion.ProtocolVersion;
        var resolution = await ModdedGameResolver.ResolveAsync(loader, protocolVersion, Timeouts.SetupTimeoutToken);

        if (resolution.Candidates.Count is 0)
        {
            _unavailable = resolution;
            return null;
        }

        var failures = new List<IntegrationTestException>();

        foreach (var setup in resolution.Candidates)
        {
            try
            {
                _servers = await StartSetupAsync(setup, Timeouts.SetupTimeoutToken);
                ModdedGameResolver.RememberStarted(loader, protocolVersion, setup);

                return new MinecraftServers(_servers, setup);
            }
            catch (IntegrationTestException exception)
            {
                // A mod can declare nothing wrong and still crash its server, such as one built against another loader build.
                failures.Add(exception);
                Console.WriteLine($"[Void.IntegrationTests] {setup} servers with {string.Join(", ", setup.Mods.Select(mod => mod.FileName))} didn't start");
            }
        }

        throw new IntegrationTestException($"No {loader} server for Minecraft {protocolVersion} started with any of its mods. First failure: {failures[0].Message}", new AggregateException(failures));
    }

    protected override async Task StopServersAsync()
    {
        await Task.WhenAll(_servers.Select(server => server.DisposeAsync().AsTask()));
    }

    private async Task<MinecraftServer[]> StartSetupAsync(GameSetup setup, CancellationToken cancellationToken)
    {
        var serverTasks = Enumerable.Range(start: 1, serverCount).Select(index => ModdedServer.CreateAsync(setup, $"server-{index}.log", cancellationToken)).ToArray();

        try
        {
            await Task.WhenAll(serverTasks);
        }
        catch
        {
            await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
            throw;
        }

        return [.. serverTasks.Select(task => task.Result)];
    }
}
