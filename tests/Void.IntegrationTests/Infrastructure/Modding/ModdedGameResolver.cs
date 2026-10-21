using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>
/// Picks the modded game a protocol version runs from what is released today: the newest of its Minecraft releases that has a loader build
/// and a content mod, so a new protocol version is covered as soon as both exist.
/// </summary>
internal static class ModdedGameResolver
{
    // A mod or loader build can declare nothing wrong and still crash its server, so several are kept to try in turn.
    private const int MaximumCandidates = 8;

    private static readonly ConcurrentDictionary<(ModLoader Loader, ProtocolVersion ProtocolVersion), CachedLookup<ModdedGameResolution>> Resolutions = new();

    public static async Task<ModdedGameResolution> ResolveAsync(ModLoader loader, ProtocolVersion protocolVersion, CancellationToken cancellationToken)
    {
        var lookup = Resolutions.GetOrAdd((loader, protocolVersion), key => new CachedLookup<ModdedGameResolution>(() => ResolveCoreAsync(key.Loader, key.ProtocolVersion)));

        return await lookup.GetAsync(cancellationToken);
    }

    private static async Task<ModdedGameResolution> ResolveCoreAsync(ModLoader loader, ProtocolVersion protocolVersion)
    {
        var cancellationToken = CancellationToken.None;
        ReleaseVersion? newestReleaseWithBuild = null;

        foreach (var release in protocolVersion.Releases.OrderDescending())
        {
            var builds = await FindBuildsAsync(loader, release, cancellationToken);

            if (builds.Count is 0)
                continue;

            newestReleaseWithBuild ??= release;
            var contentMods = await ModrinthCatalog.FindContentModsAsync(loader, release, cancellationToken);
            var candidates = new List<GameSetup>();

            // Each loader build gets the first build of every content mod that declares nothing against it, so the candidates differ by mod.
            foreach (var build in builds)
            {
                foreach (var projectBuilds in contentMods.GroupBy(contentMod => contentMod.Project))
                {
                    if (await FindCompatibleModsAsync(projectBuilds, release, build, cancellationToken) is { } mods)
                        candidates.Add(new GameSetup(release, build, mods));
                }
            }

            if (candidates.Count > 0)
                return ModdedGameResolution.Runnable([.. candidates.Take(MaximumCandidates)]);
        }

        if (newestReleaseWithBuild is null)
        {
            // A release older than the loader's newest one was passed over and never gets a build, while a newer one may get it soon.
            var newestLoaderRelease = await FindNewestReleaseAsync(loader, cancellationToken);
            var loaderNotReleasedYet = newestLoaderRelease is null || protocolVersion.FirstRelease.CompareTo(newestLoaderRelease) > 0;
            var loaderReason = loaderNotReleasedYet
                ? $"{loader} has not released a build for Minecraft {protocolVersion} yet; these tests run once it does"
                : $"{loader} never released a build for Minecraft {protocolVersion}";

            return ModdedGameResolution.Skipped(loaderReason, loaderNotReleasedYet);
        }

        var newestModRelease = await ModrinthCatalog.FindNewestReleaseAsync(loader, cancellationToken);
        var modNotReleasedYet = newestModRelease is null || newestReleaseWithBuild.CompareTo(newestModRelease) > 0;
        var projects = string.Join(", ", ModrinthCatalog.ContentModProjects);
        var modReason = modNotReleasedYet
            ? $"No content mod ({projects}) has released a {loader} build for Minecraft {protocolVersion} yet; these tests run once one does"
            : $"No content mod ({projects}) has a {loader} build that runs on Minecraft {protocolVersion}";

        return ModdedGameResolution.Skipped(modReason, modNotReleasedYet);
    }

    private static async Task<ModFile[]?> FindCompatibleModsAsync(
        IEnumerable<ContentModBuild> contentModBuilds,
        ReleaseVersion release,
        LoaderBuild build,
        CancellationToken cancellationToken
    )
    {
        foreach (var contentModBuild in contentModBuilds)
        {
            var mods = await Task.WhenAll(contentModBuild.Downloads.Select(download => ModFileCache.DownloadAsync(download, cancellationToken)));

            if (ModCompatibility.FindProblem(mods, release, build) is not { } problem)
                return mods;

            Console.WriteLine($"[Void.IntegrationTests] Passed over {string.Join(", ", mods.Select(mod => mod.FileName))} for {build}: {problem}");
        }

        return null;
    }

    private static Task<IReadOnlyList<LoaderBuild>> FindBuildsAsync(ModLoader loader, ReleaseVersion release, CancellationToken cancellationToken)
    {
        return loader is ModLoader.Forge
            ? ForgeBuildCatalog.FindBuildsAsync(release, cancellationToken)
            : NeoForgeBuildCatalog.FindBuildsAsync(release, cancellationToken);
    }

    private static Task<ReleaseVersion?> FindNewestReleaseAsync(ModLoader loader, CancellationToken cancellationToken)
    {
        return loader is ModLoader.Forge
            ? ForgeBuildCatalog.FindNewestReleaseAsync(cancellationToken)
            : NeoForgeBuildCatalog.FindNewestReleaseAsync(cancellationToken);
    }
}
