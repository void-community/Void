using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.IntegrationTests.Infrastructure.Modding;
using Void.Minecraft.Network;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// Runs servers of a mod loader for one protocol version at a time, since modded clients only join servers of their own version and mods.
/// A protocol version the loader or mods haven't released for is skipped.
/// </summary>
public abstract class ModdedServersFixture(ModLoader loader) : IMinecraftServersFixture, IAsyncLifetime
{
    // Setups whose servers started, so later tests of the protocol version don't try mods that crashed again.
    private static readonly ConcurrentDictionary<(ModLoader Loader, ProtocolVersion ProtocolVersion), GameSetup> StartedSetups = new();

    private ProtocolVersion? _protocolVersion;
    private MinecraftServers? _servers;

    public async Task<MinecraftServers> PrepareServersAsync(ProtocolVersion protocolVersion, int serverCount, CancellationToken cancellationToken)
    {
        if (_servers is not null && _protocolVersion == protocolVersion && _servers.Servers.Count >= serverCount)
            return _servers;

        await StopServersAsync();

        var resolution = await ModdedGameResolver.ResolveAsync(loader, protocolVersion, cancellationToken);

        if (resolution.Candidates.Count is 0)
            SkippedTestWarnings.Skip(resolution.SkipReason, resolution.WarnOnSkip);

        IReadOnlyList<GameSetup> candidates = StartedSetups.TryGetValue((loader, protocolVersion), out var startedSetup) ? [startedSetup] : resolution.Candidates;
        var failures = new List<IntegrationTestException>();

        foreach (var setup in candidates)
        {
            try
            {
                _servers = new MinecraftServers(await StartServersAsync(setup, serverCount, cancellationToken), setup);
                _protocolVersion = protocolVersion;
                StartedSetups[(loader, protocolVersion)] = setup;

                return _servers;
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

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopServersAsync();
        GC.SuppressFinalize(this);
    }

    private static async Task<MinecraftServer[]> StartServersAsync(GameSetup setup, int serverCount, CancellationToken cancellationToken)
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

    private async Task StopServersAsync()
    {
        if (_servers is not { } servers)
            return;

        _servers = null;
        _protocolVersion = null;
        await Task.WhenAll(servers.Servers.Select(server => server.DisposeAsync().AsTask()));
    }
}
