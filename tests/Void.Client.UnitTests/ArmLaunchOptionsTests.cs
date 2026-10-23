using System.Text.Json;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class ArmLaunchOptionsTests
{
    [Fact]
    public void UsesMinecraftLwjglVersionFromVersion34()
    {
        var plan = CreatePlan("""
            {
              "javaVersion": { "majorVersion": 25 },
              "libraries": [
                { "name": "org.lwjgl:lwjgl:3.4.3" },
                { "name": "org.lwjgl:lwjgl-sdl:3.4.3" },
                { "name": "org.lwjgl:lwjgl-vulkan:3.4.3" },
                { "name": "org.lwjgl:lwjgl:3.4.3:natives-linux" },
                { "name": "com.mojang:brigadier:1.3.10" }
              ]
            }
            """);

        Assert.False(plan.UsesLegacyLwjgl);
        Assert.Equal("3.4.3", plan.FixVersion);
        Assert.Equal(["lwjgl", "lwjgl-sdl", "lwjgl-vulkan"], plan.Modules);
        Assert.Null(plan.UnsafeCoreDownloadUri);
        Assert.Equal(25, plan.JavaMajorVersion);
    }

    [Fact]
    public void CapturesUnsafeOnlyLwjglCore()
    {
        var plan = CreatePlan("""
            {
              "javaVersion": { "majorVersion": 25 },
              "libraries": [
                { "name": "org.lwjgl:lwjgl:3.4.1:unsafe", "downloads": { "artifact": { "url": "https://libraries.minecraft.net/org/lwjgl/lwjgl/3.4.1/lwjgl-3.4.1-unsafe.jar" } } },
                { "name": "org.lwjgl:lwjgl-glfw:3.4.1" },
                { "name": "org.lwjgl:lwjgl-opengl:3.4.1" }
              ]
            }
            """);

        Assert.Equal("3.4.1", plan.FixVersion);
        Assert.Equal(["lwjgl-glfw", "lwjgl-opengl"], plan.Modules);
        Assert.Equal(new Uri("https://libraries.minecraft.net/org/lwjgl/lwjgl/3.4.1/lwjgl-3.4.1-unsafe.jar"), plan.UnsafeCoreDownloadUri);
    }

    [Theory]
    [InlineData("3.3.3", 21, "3.3.3")]
    [InlineData("3.3.2", 17, "3.3.2")]
    [InlineData("3.3.1", 17, "3.3.1")]
    [InlineData("3.2.2", 8, "3.3.3")]
    [InlineData("3.1.6", 8, "3.3.3")]
    public void UsesMinecraftLwjglVersionWhereArmNativesExist(string lwjglVersion, int javaMajorVersion, string expectedFixVersion)
    {
        // Issue #1620: Sodium for 1.20.1 exits the game unless it runs on LWJGL 3.3.1, which has ARM64 natives. 3.2.2 and older have none.
        var plan = CreatePlan($$"""
            {
              "javaVersion": { "majorVersion": {{javaMajorVersion}} },
              "libraries": [
                { "name": "org.lwjgl:lwjgl:3.2.1" },
                { "name": "org.lwjgl:lwjgl:{{lwjglVersion}}" },
                { "name": "org.lwjgl:lwjgl-glfw:{{lwjglVersion}}" }
              ]
            }
            """);

        Assert.Equal(expectedFixVersion, plan.FixVersion);
        Assert.Equal(["lwjgl", "lwjgl-glfw"], plan.Modules);
        Assert.Equal(javaMajorVersion, plan.JavaMajorVersion);
    }

    [Fact]
    public void DetectsLegacyLwjgl()
    {
        var plan = CreatePlan("""
            {
              "libraries": [
                { "name": "org.lwjgl.lwjgl:lwjgl:2.9.4-nightly-20150209" },
                { "name": "org.lwjgl.lwjgl:lwjgl_util:2.9.4-nightly-20150209" }
              ]
            }
            """);

        Assert.True(plan.UsesLegacyLwjgl);
        Assert.Null(plan.FixVersion);
        Assert.Equal(8, plan.JavaMajorVersion);
    }

    [Fact]
    public void CreatesNoArgumentsWithoutArmOptions()
    {
        Assert.Empty(GameRuntime.CreateArmPortableMinecraftArguments(options: null, requestedArguments: [], isLaunch: true));
    }

    [Fact]
    public void AddsLegacyJvmOnlyAtLaunch()
    {
        GameRuntime.ArmLaunchOptions options = new(UsesLegacyJvm: true, FixLwjglVersion: null, ExcludedLibraries: [], IncludedClassPaths: []);

        Assert.Equal(["--jvm", "/usr/local/bin/java-arm64-lwjgl2"], GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: [], isLaunch: true));
        Assert.Empty(GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: [], isLaunch: false));
        Assert.Empty(GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: ["--jvm=/custom/java"], isLaunch: true));
    }

    [Fact]
    public void AddsLwjglFixIncludesAndExcludes()
    {
        GameRuntime.ArmLaunchOptions options = new(
            UsesLegacyJvm: false,
            FixLwjglVersion: "3.4.1",
            ExcludedLibraries: ["org.lwjgl:lwjgl-vulkan:*:natives-linux-arm64"],
            IncludedClassPaths: ["/opt/portableminecraftclient/lwjgl-3.4.1-unsafe.jar", "/opt/portableminecraftclient/lwjgl-3.4.1-natives-linux-arm64.jar"]
        );

        Assert.Equal(
            [
                "--fix-lwjgl", "3.4.1",
                "--include-class", "/opt/portableminecraftclient/lwjgl-3.4.1-unsafe.jar",
                "--include-class", "/opt/portableminecraftclient/lwjgl-3.4.1-natives-linux-arm64.jar",
                "--exclude-lib", "org.lwjgl:lwjgl-vulkan:*:natives-linux-arm64"
            ],
            GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: [], isLaunch: false)
        );
    }

    [Fact]
    public void UsesSystemAllocatorWithLwjgl331()
    {
        // LWJGL 3.3.1's ARM64 jemalloc refuses 16 KiB pages, so Minecraft 1.20.1 failed to allocate its render buffers.
        GameRuntime.ArmLaunchOptions options = new(UsesLegacyJvm: false, FixLwjglVersion: "3.3.1", ExcludedLibraries: [], IncludedClassPaths: []);

        Assert.Equal(
            ["--fix-lwjgl", "3.3.1", "--jvm-arg=-Dorg.lwjgl.system.allocator=system"],
            GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: [], isLaunch: true)
        );
    }

    [Fact]
    public void RequestedArgumentsOverrideArmDefaults()
    {
        GameRuntime.ArmLaunchOptions options = new(
            UsesLegacyJvm: false,
            FixLwjglVersion: "3.4.3",
            ExcludedLibraries: ["org.lwjgl:lwjgl-vulkan:*:natives-linux-arm64"],
            IncludedClassPaths: ["/tmp/core.jar"]
        );

        Assert.Equal(
            ["--exclude-lib", "org.lwjgl:lwjgl-vulkan:*:natives-linux-arm64"],
            GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: ["--fix-lwjgl", "3.4.0"], isLaunch: true)
        );
        Assert.Equal(
            ["--fix-lwjgl", "3.4.3", "--include-class", "/tmp/core.jar"],
            GameRuntime.CreateArmPortableMinecraftArguments(options, requestedArguments: ["--exclude-lib=org.lwjgl:*"], isLaunch: true)
        );
    }

    [Theory]
    [InlineData(8, 8, true)]
    [InlineData(8, 17, false)]
    [InlineData(21, 25, true)]
    [InlineData(25, 21, false)]
    public void MatchesPortableMinecraftJavaCompatibility(int requiredMajorVersion, int installedMajorVersion, bool expectedCompatible)
    {
        Assert.Equal(expectedCompatible, GameRuntime.IsCompatibleJavaMajorVersion(requiredMajorVersion, installedMajorVersion));
    }

    [Theory]
    [InlineData("mojang:1.21.1", "1.21.1")]
    [InlineData("mojang:", "release")]
    [InlineData("neoforge:", "release")]
    [InlineData("neoforge:26.3", "26.3")]
    [InlineData("neoforge::26.3.0.89", "26.3")]
    [InlineData("neoforge::26.3.1.4", "26.3.1")]
    [InlineData("neoforge::21.1.200", "1.21.1")]
    [InlineData("neoforge::21.0.167", "1.21")]
    [InlineData("neoforge::47.1.82", "1.20.1")]
    [InlineData("neoforge::1.20.1-47.1.106", "1.20.1")]
    [InlineData("forge::1.20.1-47.2.0", "1.20.1")]
    [InlineData("fabric:1.21.1:0.16.0", "1.21.1")]
    [InlineData("unknown:", null)]
    public void ReadsMinecraftVersionFromPortableVersion(string portableMinecraftVersion, string? expectedMinecraftVersion)
    {
        Assert.Equal(expectedMinecraftVersion, GameRuntime.GetMinecraftVersionFromPortableVersion(portableMinecraftVersion));
    }

    [Fact]
    public void SelectsAzulArmJreWithoutJavaFx()
    {
        using var packages = JsonDocument.Parse("""
            [
              { "name": "zulu25.30.17-ca-fx-jre25.0.1-linux_aarch64.tar.gz", "download_url": "https://cdn.azul.com/zulu/bin/fx.tar.gz" },
              { "name": "zulu25.30.17-ca-jre25.0.1-linux_x64.tar.gz", "download_url": "https://cdn.azul.com/zulu/bin/x64.tar.gz" },
              { "name": "zulu25.30.17-ca-jre25.0.1-linux_aarch64.tar.gz", "download_url": "https://cdn.azul.com/zulu/bin/arm64.tar.gz" }
            ]
            """);

        Assert.Equal("https://cdn.azul.com/zulu/bin/arm64.tar.gz", GameRuntime.SelectAzulJreDownloadUrl(packages.RootElement));
    }

    private static GameRuntime.ArmLwjglPlan CreatePlan(string versionMetadataJson)
    {
        using var versionMetadata = JsonDocument.Parse(versionMetadataJson);

        return GameRuntime.CreateArmLwjglPlan(versionMetadata.RootElement);
    }
}
