using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class PortableMinecraftVersionTests
{
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
