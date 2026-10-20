using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Finds NeoForge builds, the newest stable one of a Minecraft release, or its newest when none is stable yet.</summary>
internal static class NeoForgeBuildCatalog
{
    // NeoForge for Minecraft 1.20.1 was published as a fork of Forge, with Forge's version names.
    private const string LegacyVersionPrefix = "1.20.1-";
    private static readonly ReleaseVersion LegacyRelease = new(Major: 1, Minor: 20, Patch: 1);
    private static readonly Uri MavenMetadataUri = new("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml");
    private static readonly Uri LegacyMavenMetadataUri = new("https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml");
    private static readonly CachedLookup<IReadOnlyList<string>> Versions = new(LoadVersionsAsync);

    public static async Task<IReadOnlyList<LoaderBuild>> FindBuildsAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        var builds = (await Versions.GetAsync(cancellationToken)).Where(version => FindRelease(version) == release).ToArray();
        var build = builds.Where(IsStable).MaxBy(BuildNumber) ?? builds.MaxBy(BuildNumber);

        // Servers install NeoForge for 1.20.1 by its build alone, such as 47.1.106.
        return build is null ? [] : [new LoaderBuild(ModLoader.NeoForge, ClientLoaderVersion: build, ServerLoaderVersion: BuildName(build))];
    }

    public static async Task<ReleaseVersion?> FindNewestReleaseAsync(CancellationToken cancellationToken)
    {
        return (await Versions.GetAsync(cancellationToken)).Select(FindRelease).OfType<ReleaseVersion>().Max();
    }

    /// <summary>Returns the Minecraft release a build is for: 21.1.x is for 1.21.1, 21.0.x for 1.21, and 26.3.0.x for 26.3.</summary>
    private static ReleaseVersion? FindRelease(string version)
    {
        if (version.StartsWith(LegacyVersionPrefix, StringComparison.Ordinal))
            return LegacyRelease;

        var components = version.Split('-')[0].Split('.');

        if (components.Length < 3 || !int.TryParse(components[0], out var major) || !int.TryParse(components[1], out var minor) || !int.TryParse(components[2], out var patch))
            return null;

        return major >= 26 ? new ReleaseVersion(major, minor, patch) : new ReleaseVersion(Major: 1, major, minor);
    }

    private static bool IsStable(string version)
    {
        return !BuildName(version).Contains('-');
    }

    private static Version BuildNumber(string version)
    {
        return Version.TryParse(BuildName(version).Split('-')[0], out var number) ? number : new Version();
    }

    private static string BuildName(string version)
    {
        return version.StartsWith(LegacyVersionPrefix, StringComparison.Ordinal) ? version[LegacyVersionPrefix.Length..] : version;
    }

    private static async Task<IReadOnlyList<string>> LoadVersionsAsync()
    {
        var metadataXml = await UpstreamHttp.GetStringAsync(MavenMetadataUri, CancellationToken.None);
        var legacyMetadataXml = await UpstreamHttp.GetStringAsync(LegacyMavenMetadataUri, CancellationToken.None);

        return [.. new[] { metadataXml, legacyMetadataXml }.SelectMany(xml => XDocument.Parse(xml).Descendants("version").Select(version => version.Value.Trim()))];
    }
}
