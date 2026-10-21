using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class PortableMinecraftVersionTests
{
    [Theory]
    [InlineData("forge", "1.20.1", null, "forge:1.20.1")]
    [InlineData("forge", "1.20.1", "", "forge:1.20.1")]
    [InlineData("forge", "1.20.1", "47.4.0", "forge::1.20.1-47.4.0")]
    [InlineData("forge", "1.20.1", "1.20.1-47.4.0", "forge::1.20.1-47.4.0")]
    [InlineData("forge", "1.7.10", "10.13.4.1614-1.7.10", "forge::1.7.10-10.13.4.1614-1.7.10")]
    [InlineData("fabric", "1.21.1", null, "fabric:1.21.1")]
    [InlineData("fabric", "1.21.1", "0.16.5", "fabric:1.21.1:0.16.5")]
    [InlineData("quilt", "1.21.1", "0.26.4", "quilt:1.21.1:0.26.4")]
    [InlineData("legacyfabric", "1.12.2", null, "legacyfabric:1.12.2")]
    public void CreatesTheVersionOfAModLoader(string loader, string minecraftVersion, string? loaderVersion, string expectedVersion)
    {
        Assert.Equal(expectedVersion, GameRuntime.CreatePortableMinecraftVersion(loader, minecraftVersion, loaderVersion));
    }

    [Theory]
    [InlineData("1.7.10", "10.13.4.1614", "1.7.10-10.13.4.1614-1.7.10")]
    [InlineData("1.7.2", "10.12.2.1161", "1.7.2-10.12.2.1161-mc172")]
    [InlineData("1.12.2", "14.23.5.2859", "1.12.2-14.23.5.2859")]
    [InlineData("1.12.2", "1.12.2-14.23.5.2859", "1.12.2-14.23.5.2859")]
    [InlineData("1.20.1", "47.1.106", "1.20.1-47.1.106")]
    [InlineData("1.21.1", "21.1.264", "21.1.264")]
    [InlineData("26.3", "26.3.0.95", "26.3.0.95")]
    [InlineData("1.12.2", "14.23.5.285", null)]
    [InlineData("1.12.1", "14.23.5.2859", null)]
    public void FindsALoaderBuildAsServersNameIt(string minecraftVersion, string loaderVersion, string? expectedVersion)
    {
        // PortableMC searches by part of the name, so builds whose names only start alike are listed too.
        string[][] output =
        [
            ["row", "version", "game_version", "channel"],
            ["sep"],
            ["row", "1.7.10-10.13.4.1614-1.7.10", "1.7.10", "stable"],
            ["row", "1.7.2-10.12.2.1161-mc172", "1.7.2", "stable"],
            ["row", "1.12.2-14.23.5.2859", "1.12.2", "stable"],
            ["row", "1.12.2-14.23.5.2855", "1.12.2", "stable"],
            ["row", "1.20.1-47.1.106", "1.20.1", "stable"],
            ["row", "21.1.264", "1.21.1", "stable"],
            ["row", "26.3.0.95", "26.3", "stable"]
        ];

        Assert.Equal(expectedVersion, GameRuntime.FindPortableMinecraftLoaderVersion(output, minecraftVersion, loaderVersion));
    }

    [Fact]
    public void ReadsTheInstalledVersionFromTheLastLoadedHierarchy()
    {
        // The Forge installer loads Minecraft alone before the version it installs.
        string[][] output =
        [
            ["loaded_hierarchy", "1.20.1"],
            ["installed_version", "forge-1.20.1-47.4.0"],
            ["loaded_hierarchy", "forge-1.20.1-47.4.0", "1.20.1"]
        ];

        Assert.Equal(new GameRuntime.PortableMinecraftInstallation("forge-1.20.1-47.4.0", "1.20.1"), GameRuntime.ParseLoadedHierarchy(output));
        Assert.Equal(new GameRuntime.PortableMinecraftInstallation("1.21.1", "1.21.1"), GameRuntime.ParseLoadedHierarchy([["loaded_hierarchy", "1.21.1"]]));
        Assert.Null(GameRuntime.ParseLoadedHierarchy([["loaded_hierarchy"], ["installed_version", "1.21.1"]]));
    }
}
