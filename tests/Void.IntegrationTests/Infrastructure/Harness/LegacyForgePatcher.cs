using System;
using System.Runtime.InteropServices;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/LegacyForgePatcher, without which Forge for Minecraft 1.7.2 stops with a ConcurrentModificationException
/// on Java 8u20 and later, because its FML sorts LaunchWrapper's tweakers while LaunchWrapper iterates them.
/// </summary>
internal static class LegacyForgePatcher
{
    /// <summary>Where servers keep the agent.</summary>
    public const string ServerPath = "/opt/legacyforgepatcher/legacyforgepatcher.so";

    private static readonly ReleaseVersion PatchedRelease = new(Major: 1, Minor: 7, Patch: 2);

    /// <summary>The agent of the latest release, for the architecture of the Docker host, which runs the tests too.</summary>
    public static Uri Url { get; } = new($"https://github.com/void-community/LegacyForgePatcher/releases/latest/download/legacyforgepatcher-{(RuntimeInformation.OSArchitecture is Architecture.Arm64 ? "linux-arm64" : "linux-x64")}.so");

    public static bool AppliesTo(ModLoader loader, ReleaseVersion release)
    {
        return loader is ModLoader.Forge && release == PatchedRelease;
    }
}
