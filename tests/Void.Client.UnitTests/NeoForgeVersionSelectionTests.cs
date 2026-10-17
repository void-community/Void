using System.Text.Json;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class NeoForgeVersionSelectionTests
{
    private static readonly string[] ReleasesNewestFirst = ["26.3", "26.2", "26.1.2", "1.21.11", "1.21.1", "1.21", "1.20.1"];

    [Theory]
    [InlineData("26.3", "26.3.0.")]
    [InlineData("26.3.1", "26.3.1.")]
    [InlineData("26.1", "26.1.0.")]
    [InlineData("1.21.1", "21.1.")]
    [InlineData("1.21", "21.0.")]
    [InlineData("1.20.4", "20.4.")]
    [InlineData("1.20.1", "1.20.1-")]
    [InlineData("26.4-snapshot-1", null)]
    [InlineData("26.3-rc-1", null)]
    [InlineData("26.3-pre-2", null)]
    [InlineData("1.21-pre1", null)]
    [InlineData("25w21a", null)]
    public void CreatesNeoForgeVersionPrefix(string minecraftVersion, string? expectedPrefix)
    {
        Assert.Equal(expectedPrefix, GameRuntime.CreateNeoForgeVersionPrefix(minecraftVersion));
    }

    [Theory]
    [InlineData("26.3.0.89", true)]
    [InlineData("1.20.1-47.1.106", true)]
    [InlineData("26.3.0.75-beta", false)]
    [InlineData("21.0.0-alpha.1.21.pre1", false)]
    public void DetectsStableNeoForgeVersions(string neoForgeVersion, bool expectedStable)
    {
        Assert.Equal(expectedStable, GameRuntime.IsStableNeoForgeVersion(neoForgeVersion));
    }

    [Fact]
    public void FindsNumericallyLatestStableNeoForgeVersion()
    {
        string[] neoForgeVersions = ["26.3.0.9", "26.3.0.89", "26.3.0.90-beta", "26.2.0.120", "26.3.1.1"];

        Assert.Equal("26.3.0.89", GameRuntime.FindLatestStableNeoForgeVersion(neoForgeVersions, minecraftVersion: "26.3"));
    }

    [Fact]
    public void FindsNoStableNeoForgeVersionWhenOnlyBetasExist()
    {
        string[] neoForgeVersions = ["26.3.0.0-beta", "26.3.0.75-beta"];

        Assert.Null(GameRuntime.FindLatestStableNeoForgeVersion(neoForgeVersions, minecraftVersion: "26.3"));
    }

    [Fact]
    public void FallsBackToPreviousReleaseWhileNewestReleaseHasOnlyBetas()
    {
        string[] neoForgeVersions = ["26.3.0.75-beta", "26.2.0.120", "26.2.0.7"];

        var selection = GameRuntime.SelectLatestStableNeoForge(ReleasesNewestFirst, neoForgeVersions, maximumMinecraftVersion: null);

        Assert.Equal("26.2", selection.MinecraftVersion);
        Assert.Equal("26.2.0.120", selection.NeoForgeVersion);
        Assert.Equal(["26.3"], selection.SkippedMinecraftVersions);
    }

    [Fact]
    public void StartsSelectionAtMaximumMinecraftVersion()
    {
        string[] neoForgeVersions = ["26.3.0.89", "26.2.0.120", "21.1.200"];

        var selection = GameRuntime.SelectLatestStableNeoForge(ReleasesNewestFirst, neoForgeVersions, maximumMinecraftVersion: "26.2");

        Assert.Equal("26.2", selection.MinecraftVersion);
        Assert.Equal("26.2.0.120", selection.NeoForgeVersion);
        Assert.Empty(selection.SkippedMinecraftVersions);
    }

    [Fact]
    public void SelectsNewestReleaseWhenItHasStableBuild()
    {
        string[] neoForgeVersions = ["26.3.0.89", "26.2.0.120"];

        var selection = GameRuntime.SelectLatestStableNeoForge(ReleasesNewestFirst, neoForgeVersions, maximumMinecraftVersion: "26.3");

        Assert.Equal("26.3", selection.MinecraftVersion);
        Assert.Equal("26.3.0.89", selection.NeoForgeVersion);
    }

    [Fact]
    public void RejectsUnknownMaximumMinecraftVersion()
    {
        Assert.Throws<InvalidOperationException>(() => GameRuntime.SelectLatestStableNeoForge(ReleasesNewestFirst, ["26.3.0.89"], maximumMinecraftVersion: "26.4-snapshot-1"));
    }

    [Fact]
    public void FailsWhenNoReleaseHasStableBuild()
    {
        Assert.Throws<InvalidOperationException>(() => GameRuntime.SelectLatestStableNeoForge(ReleasesNewestFirst, ["26.3.0.1-beta"], maximumMinecraftVersion: null));
    }

    [Fact]
    public void ParsesMavenMetadataVersions()
    {
        const string metadataXml = """
            <metadata>
              <versioning>
                <versions>
                  <version>26.3.0.0-beta</version>
                  <version>26.3.0.89</version>
                </versions>
              </versioning>
            </metadata>
            """;

        Assert.Equal(["26.3.0.0-beta", "26.3.0.89"], GameRuntime.ParseMavenMetadataVersions(metadataXml));
    }

    [Fact]
    public void ParsesMojangVersionManifestInManifestOrder()
    {
        const string manifestJson = """
            {
              "latest": { "release": "26.3", "snapshot": "26.4-snapshot-4" },
              "versions": [
                { "id": "26.4-snapshot-4", "type": "snapshot", "url": "https://piston-meta.mojang.com/v1/packages/a/26.4-snapshot-4.json" },
                { "id": "26.3", "type": "release", "url": "https://piston-meta.mojang.com/v1/packages/b/26.3.json" },
                { "id": "26.2", "type": "release", "url": "https://piston-meta.mojang.com/v1/packages/c/26.2.json" }
              ]
            }
            """;

        var manifest = GameRuntime.ParseMojangVersionManifest(manifestJson);

        Assert.Equal("26.3", manifest.LatestRelease);
        Assert.Equal("26.4-snapshot-4", manifest.LatestSnapshot);
        Assert.Equal(["26.4-snapshot-4", "26.3", "26.2"], manifest.Versions.Select(version => version.Id));
        Assert.Equal("release", manifest.Versions[1].Type);
        Assert.Equal(new Uri("https://piston-meta.mojang.com/v1/packages/b/26.3.json"), manifest.Versions[1].MetadataUri);
    }

    [Fact]
    public void PrefersNewestSodiumReleaseOverNewerPreRelease()
    {
        using var versions = JsonDocument.Parse("""
            [
              { "version_number": "mc26.3-0.9.3-beta.1-neoforge", "version_type": "beta", "files": [{ "primary": true, "url": "https://cdn.modrinth.com/beta.jar" }] },
              { "version_number": "mc26.3-0.9.2-neoforge", "version_type": "release", "files": [{ "primary": true, "url": "https://cdn.modrinth.com/release.jar" }] }
            ]
            """);

        var download = GameRuntime.SelectSodiumDownload(versions.RootElement);

        Assert.NotNull(download);
        Assert.Equal("mc26.3-0.9.2-neoforge", download.VersionNumber);
        Assert.Equal(new Uri("https://cdn.modrinth.com/release.jar"), download.DownloadUri);
    }

    [Fact]
    public void FallsBackToSodiumPreReleaseWhenNoReleaseExists()
    {
        using var versions = JsonDocument.Parse("""
            [
              { "version_number": "mc26.4-0.9.4-alpha.1-neoforge", "version_type": "alpha", "files": [{ "primary": true, "url": "https://cdn.modrinth.com/alpha.jar" }] }
            ]
            """);

        Assert.Equal("alpha", GameRuntime.SelectSodiumDownload(versions.RootElement)?.VersionType);
    }

    [Fact]
    public void SelectsNoSodiumWhenNoneIsPublished()
    {
        using var versions = JsonDocument.Parse("[]");

        Assert.Null(GameRuntime.SelectSodiumDownload(versions.RootElement));
    }
}
