using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Nito.AsyncEx;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Finds the Forge builds of a Minecraft release worth playing on: its newest one, which mods are built against more often, then the recommended one.</summary>
internal static class ForgeBuildCatalog
{
    private const string MavenBaseUri = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private static readonly Uri PromotionsUri = new("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
    private static readonly Uri MavenMetadataUri = new($"{MavenBaseUri}/maven-metadata.xml");
    private static readonly AsyncLazy<ForgeBuilds> Builds = new(LoadBuildsAsync, AsyncLazyFlags.RetryOnFailure);

    public static async Task<IReadOnlyList<LoaderBuild>> FindBuildsAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        var builds = await Builds.Task.WaitAsync(cancellationToken);
        var promotedBuilds = new[] { $"{release}-latest", $"{release}-recommended" }
            .Select(promotion => builds.Promotions.GetValueOrDefault(promotion))
            .OfType<string>()
            .Distinct();

        return [.. promotedBuilds.Select(promotedBuild => CreateBuild(builds, release, promotedBuild)).OfType<LoaderBuild>()];
    }

    public static async Task<ReleaseVersion?> FindNewestReleaseAsync(CancellationToken cancellationToken)
    {
        var builds = await Builds.Task.WaitAsync(cancellationToken);

        return builds.Promotions.Keys
            .Select(promotion => ReleaseVersion.TryParse(promotion[..promotion.LastIndexOf('-')], out var release) ? release : null)
            .OfType<ReleaseVersion>()
            .Max();
    }

    private static LoaderBuild? CreateBuild(ForgeBuilds builds, ReleaseVersion release, string promotedBuild)
    {
        // Old builds name their artifact after the Minecraft version too, such as 1.7.10-10.13.4.1614-1.7.10.
        var artifactPrefix = $"{release}-{promotedBuild}";
        var artifact = builds.Versions.FirstOrDefault(version => version == artifactPrefix || version.StartsWith($"{artifactPrefix}-", StringComparison.Ordinal));

        // Servers run the build's own installer, since installing by version misses builds with such names.
        return artifact is null
            ? null
            : new LoaderBuild(ModLoader.Forge, artifact[($"{release}-".Length)..], promotedBuild, new Uri($"{MavenBaseUri}/{artifact}/forge-{artifact}-installer.jar"));
    }

    private static async Task<ForgeBuilds> LoadBuildsAsync()
    {
        var promotionsJson = await UpstreamHttp.GetStringAsync(PromotionsUri, CancellationToken.None);
        var metadataXml = await UpstreamHttp.GetStringAsync(MavenMetadataUri, CancellationToken.None);

        using var promotionsDocument = JsonDocument.Parse(promotionsJson);
        var promotions = promotionsDocument.RootElement.GetProperty("promos").EnumerateObject().ToDictionary(promotion => promotion.Name, promotion => promotion.Value.GetString() ?? "");
        var versions = XDocument.Parse(metadataXml).Descendants("version").Select(version => version.Value.Trim()).ToArray();

        return new ForgeBuilds(promotions, versions);
    }

    private sealed record ForgeBuilds(IReadOnlyDictionary<string, string> Promotions, IReadOnlyList<string> Versions);
}
