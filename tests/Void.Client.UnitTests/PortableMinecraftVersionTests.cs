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
