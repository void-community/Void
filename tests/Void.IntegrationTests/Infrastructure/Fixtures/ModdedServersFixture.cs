using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.IntegrationTests.Infrastructure.Modding;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// Runs servers of a mod loader for one protocol version, since modded clients only join servers of their own version and mods. A protocol
/// version the loader or mods haven't released for starts nothing, and its tests skip.
/// </summary>
public abstract class ModdedServersFixture<TProtocolVersion>(ModLoader loader, int serverCount) : IMinecraftServersFixture, IAsyncLifetime where TProtocolVersion : IProtocolVersionType
{
    private ModdedGameResolution? _unavailable;
    private MinecraftServers? _servers;

    public bool IsAvailable => _servers is not null;
    public MinecraftServers Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");

    public void SkipIfUnavailable()
    {
        if (_unavailable is { } resolution)
            SkippedTestWarnings.Skip(resolution.SkipReason, resolution.WarnOnSkip);
    }

    public async ValueTask InitializeAsync()
    {
        var protocolVersion = TProtocolVersion.ProtocolVersion;
        var resolution = await ModdedGameResolver.ResolveAsync(loader, protocolVersion, Timeouts.SetupTimeoutToken);

        if (resolution.Candidates.Count is 0)
        {
            _unavailable = resolution;
            return;
        }

        var failures = new List<IntegrationTestException>();

        foreach (var setup in resolution.Candidates)
        {
            try
            {
                _servers = new MinecraftServers(await StartSetupAsync(setup, Timeouts.SetupTimeoutToken), setup);
                return;
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

    public async ValueTask DisposeAsync()
    {
        if (_servers is { } servers)
            await Task.WhenAll(servers.Servers.Select(server => server.DisposeAsync().AsTask()));

        GC.SuppressFinalize(this);
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
