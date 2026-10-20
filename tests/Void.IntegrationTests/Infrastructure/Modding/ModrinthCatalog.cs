using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Finds a content mod for a mod loader and Minecraft release on Modrinth, with the mods it requires.</summary>
internal static class ModrinthCatalog
{
    private const string ApiBaseUri = "https://api.modrinth.com/v2";

    // Newer builds of a mod are sometimes listed for a release they were not built for, while an older one was.
    private const int VersionsPerProject = 5;

    /// <summary>
    /// Mods that add blocks and items and must be installed on both sides, tried in order. Together they have a build for every Forge and
    /// NeoForge release so far, and the first one keeps up with new releases.
    /// </summary>
    public static readonly IReadOnlyList<string> ContentModProjects = ["storagedrawers", "refined-storage", "blood-magic", "comforts", "waddles", "trash-cans", "tool-kit"];

    // Modrinth only has development builds of Chameleon, which Storage Drawers needs up to 1.12.2 and which crash a real game.
    private static readonly IReadOnlySet<string> UnusableProjects = new HashSet<string> { "rZrAKGe1" };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static readonly ConcurrentDictionary<string, CachedLookup<ModrinthVersion[]>> VersionLookups = new();

    /// <returns>Builds of the content mods for the release, each with the mods it requires, in the order to try them.</returns>
    public static async Task<IReadOnlyList<ContentModBuild>> FindContentModsAsync(ModLoader loader, ReleaseVersion release, CancellationToken cancellationToken)
    {
        var builds = new List<ContentModBuild>();

        foreach (var project in ContentModProjects)
        {
            foreach (var version in OrderVersions(await GetVersionsAsync(project, loader, release, cancellationToken), release).Take(VersionsPerProject))
            {
                var downloads = new List<ModDownload>();

                if (await TryAddAsync(version, loader, release, downloads, [], cancellationToken))
                    builds.Add(new ContentModBuild(project, downloads));
            }
        }

        return builds;
    }

    /// <summary>Returns the newest Minecraft release any of the content mods has a build of for <paramref name="loader"/>.</summary>
    public static async Task<ReleaseVersion?> FindNewestReleaseAsync(ModLoader loader, CancellationToken cancellationToken)
    {
        var projectVersions = await Task.WhenAll(ContentModProjects.Select(project => GetVersionsAsync(project, loader, release: null, cancellationToken)));

        return projectVersions
            .SelectMany(versions => versions)
            .SelectMany(version => version.GameVersions)
            .Select(gameVersion => ReleaseVersion.TryParse(gameVersion, out var release) ? release : null)
            .OfType<ReleaseVersion>()
            .Max();
    }

    private static async Task<bool> TryAddAsync(
        ModrinthVersion version,
        ModLoader loader,
        ReleaseVersion release,
        List<ModDownload> downloads,
        HashSet<string> addedProjects,
        CancellationToken cancellationToken
    )
    {
        if (version.Files.Length is 0 || UnusableProjects.Contains(version.ProjectId))
            return false;

        if (!addedProjects.Add(version.ProjectId))
            return true;

        var file = version.Files.FirstOrDefault(file => file.Primary) ?? version.Files[0];
        downloads.Add(new ModDownload(version.ProjectId, version.VersionNumber, file.Filename, file.Hashes.Sha512, new Uri(file.Url)));

        foreach (var dependency in version.Dependencies.Where(dependency => dependency.DependencyType is "required"))
        {
            var dependencyVersion = dependency switch
            {
                { VersionId: { } versionId } => await GetVersionAsync(versionId, cancellationToken),
                { ProjectId: { } projectId } => OrderVersions(await GetVersionsAsync(projectId, loader, release, cancellationToken), release).FirstOrDefault(),
                _ => null
            };

            // A mod whose dependencies can't be installed can't run, so the next build is tried instead.
            if (dependencyVersion is null || !await TryAddAsync(dependencyVersion, loader, release, downloads, addedProjects, cancellationToken))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Orders builds made for the release first, since a build listed for several releases was often made for the newest of them, then
    /// releases before betas and alphas, then newest first.
    /// </summary>
    private static IEnumerable<ModrinthVersion> OrderVersions(ModrinthVersion[] versions, ReleaseVersion release)
    {
        return versions
            .Select((version, index) => (Version: version, Index: index))
            .OrderBy(item => NewestGameVersion(item.Version) == release ? 0 : 1)
            .ThenBy(item => item.Version.VersionType is "release" ? 0 : 1)
            .ThenBy(item => item.Index)
            .Select(item => item.Version);
    }

    private static ReleaseVersion? NewestGameVersion(ModrinthVersion version)
    {
        return version.GameVersions.Select(gameVersion => ReleaseVersion.TryParse(gameVersion, out var release) ? release : null).OfType<ReleaseVersion>().Max();
    }

    private static Task<ModrinthVersion[]> GetVersionsAsync(string project, ModLoader loader, ReleaseVersion? release, CancellationToken cancellationToken)
    {
        var query = $"loaders={Uri.EscapeDataString($"[\"{loader.ToString().ToLowerInvariant()}\"]")}";

        if (release is not null)
            query += $"&game_versions={Uri.EscapeDataString($"[\"{release}\"]")}";

        var uri = new Uri($"{ApiBaseUri}/project/{Uri.EscapeDataString(project)}/version?{query}");
        var lookup = VersionLookups.GetOrAdd(uri.AbsoluteUri, _ => new CachedLookup<ModrinthVersion[]>(() => ReadAsync<ModrinthVersion[]>(uri)));

        return lookup.GetAsync(cancellationToken);
    }

    private static async Task<ModrinthVersion?> GetVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        return await ReadAsync<ModrinthVersion>(new Uri($"{ApiBaseUri}/version/{Uri.EscapeDataString(versionId)}")).WaitAsync(cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(Uri uri)
    {
        var json = await UpstreamHttp.GetStringAsync(uri, CancellationToken.None);

        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException($"Modrinth returned no data for {uri}");
    }

    private sealed record ModrinthVersion(
        string ProjectId,
        string VersionNumber,
        string VersionType,
        string[] GameVersions,
        ModrinthFile[] Files,
        ModrinthDependency[] Dependencies
    );

    private sealed record ModrinthFile(string Url, string Filename, bool Primary, ModrinthHashes Hashes);

    private sealed record ModrinthHashes(string Sha512);

    private sealed record ModrinthDependency(string? VersionId, string? ProjectId, string DependencyType);
}
