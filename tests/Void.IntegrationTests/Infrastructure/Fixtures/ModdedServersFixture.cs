using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Void.IntegrationTests.Infrastructure.Modding;
using Void.Minecraft;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Fixtures;

/// <summary>
/// Runs servers of a mod loader for one protocol version, since modded clients only join servers of their own version and mods. The image
/// picks the loader build and the content mod's build for the newest release of the version it can, and clients copy what it installed. A
/// version the loader or mods haven't released for starts nothing, and its tests skip.
/// </summary>
public abstract class ModdedServersFixture<TProtocolVersion>(ModLoader loader, int serverCount) : IMinecraftServersFixture, IAsyncLifetime where TProtocolVersion : IProtocolVersionType
{
    /// <summary>
    /// Mods that add blocks and items and must be installed on both sides, tried in order. Together they have a build for every Forge and
    /// NeoForge release so far, and the first one keeps up with new releases.
    /// </summary>
    private static readonly string[] ContentModProjects = ["storagedrawers", "refined-storage", "blood-magic", "comforts", "waddles", "trash-cans", "tool-kit"];

    // Loaders and mods catch up with a Minecraft release within months, so one older than this that they have no build for was passed over.
    private static readonly TimeSpan CatchUpPeriod = TimeSpan.FromDays(183);

    // The image rejects a release its loader has no build for, or a mod has no build for, before the server starts.
    private const string ModMissingMessage = "Invalid parameter provided for 'modrinth' command";
    private readonly string _loaderMissingMessage = $"Invalid parameter provided for 'install-{loader.ToString().ToLowerInvariant()}' command";

    private (string Reason, bool Warn)? _skip;
    private MinecraftServers? _servers;

    public bool IsAvailable => _servers is not null;
    public MinecraftServers Servers => _servers ?? throw new InvalidOperationException($"{nameof(Servers)} are not started.");

    public void SkipIfUnavailable()
    {
        if (_skip is { } skip)
            SkippedTestWarnings.Skip(skip.Reason, skip.Warn);
    }

    public async ValueTask InitializeAsync()
    {
        var protocolVersion = TProtocolVersion.ProtocolVersion;
        var releases = protocolVersion.Releases.OrderDescending().ToArray();
        var failures = new List<IntegrationTestException>();
        var loaderReleased = false;

        foreach (var release in releases)
        {
            var outcome = await TryStartAsync(release, failures);

            if (outcome is StartOutcome.Started)
                return;

            loaderReleased |= outcome is StartOutcome.ContentModsMissing;
        }

        // A mod that crashed its server needs a look, unlike one that has no build for the version.
        if (failures.Count > 0)
            throw new IntegrationTestException($"No {loader} server for Minecraft {protocolVersion} started with any content mod. First failure: {failures[0].Message}", new AggregateException(failures));

        var releaseAge = DateTimeOffset.UtcNow - await MojangVersionCatalog.FindReleaseTimeAsync(releases[0], Timeouts.SetupTimeoutToken);
        var notReleasedYet = releaseAge < CatchUpPeriod;
        var projects = string.Join(", ", ContentModProjects);

        _skip = (loaderReleased, notReleasedYet) switch
        {
            (false, true) => ($"{loader} has not released a build for Minecraft {protocolVersion} yet; these tests run once it does", true),
            (false, false) => ($"{loader} never released a build for Minecraft {protocolVersion}", false),
            (true, true) => ($"No content mod ({projects}) has released a {loader} build for Minecraft {protocolVersion} yet; these tests run once one does", true),
            (true, false) => ($"No content mod ({projects}) has a {loader} build for Minecraft {protocolVersion}", false)
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_servers is { } servers)
            await Task.WhenAll(servers.Servers.Select(server => server.DisposeAsync().AsTask()));

        GC.SuppressFinalize(this);
    }

    private async Task<StartOutcome> TryStartAsync(ReleaseVersion release, List<IntegrationTestException> failures)
    {
        foreach (var contentModProject in ContentModProjects)
        {
            try
            {
                _servers = await StartServersAsync(release, contentModProject, Timeouts.SetupTimeoutToken);
                return StartOutcome.Started;
            }
            catch (IntegrationTestException exception) when (exception.Message.Contains(_loaderMissingMessage, StringComparison.Ordinal))
            {
                return StartOutcome.LoaderMissing;
            }
            catch (IntegrationTestException exception) when (exception.Message.Contains(ModMissingMessage, StringComparison.Ordinal))
            {
                Console.WriteLine($"[Void.IntegrationTests] {contentModProject} has no {loader} build for Minecraft {release}");
            }
            catch (IntegrationTestException exception)
            {
                // A mod can declare nothing wrong and still crash its server, such as one built against another loader build.
                failures.Add(exception);
                Console.WriteLine($"[Void.IntegrationTests] {loader} servers for Minecraft {release} with {contentModProject} didn't start");
            }
        }

        return StartOutcome.ContentModsMissing;
    }

    private async Task<MinecraftServers> StartServersAsync(ReleaseVersion release, string contentModProject, CancellationToken cancellationToken)
    {
        var serverTasks = Enumerable.Range(start: 1, serverCount).Select(index => ModdedServer.CreateAsync(loader, release, contentModProject, $"server-{index}.log", cancellationToken)).ToArray();

        try
        {
            await Task.WhenAll(serverTasks);

            var servers = serverTasks.Select(task => task.Result).ToArray();
            var setups = await Task.WhenAll(servers.Select(server => server.ReadSetupAsync(cancellationToken)));

            // Each server picks its game when it starts, while clients play one game on all of them.
            if (setups.Any(setup => setup.Loader != setups[0].Loader || !setup.Mods.Select(mod => mod.FileName).SequenceEqual(setups[0].Mods.Select(mod => mod.FileName))))
                throw new IntegrationTestException($"{loader} servers for Minecraft {release} with {contentModProject} installed different games: {string.Join("; ", setups.Select(setup => $"{setup} with {string.Join(", ", setup.Mods.Select(mod => mod.FileName))}"))}");

            return new MinecraftServers(servers, setups[0]);
        }
        catch
        {
            await Task.WhenAll(serverTasks.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result.DisposeAsync().AsTask()));
            throw;
        }
    }

    private enum StartOutcome
    {
        Started,
        LoaderMissing,
        ContentModsMissing
    }
}
