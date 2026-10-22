using System;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/LegacyForgePatcher, without which Forge for Minecraft 1.7.2 stops with a ConcurrentModificationException
/// on Java 8u20 and later, because its FML sorts LaunchWrapper's tweakers while LaunchWrapper iterates them, and Forge servers for Minecraft
/// 1.12 under load drop players at login with "Can't serialize unregistered packet", because FML switches their connections to the play state
/// before their login packets are sent.
/// </summary>
internal static class LegacyForgePatcher
{
    /// <summary>Where servers keep the agent.</summary>
    public const string ServerPath = "/opt/legacyforgepatcher/legacyforgepatcher.jar";

    private static readonly ReleaseVersion PatchedRelease = new(Major: 1, Minor: 7, Patch: 2);

    /// <summary>The Java agent of the latest release, a jar that loads with <c>-javaagent</c> on every architecture.</summary>
    public static Uri Url { get; } = new("https://github.com/void-community/LegacyForgePatcher/releases/latest/download/legacyforgepatcher.jar");

    /// <summary>Whether Forge's game needs the agent, which only Forge for Minecraft 1.7.2 does, to start at all.</summary>
    public static bool AppliesTo(ModLoader loader, ReleaseVersion release)
    {
        return loader is ModLoader.Forge && release == PatchedRelease;
    }

    /// <summary>Whether a Forge server needs the agent: one for Minecraft 1.7.2 to start, and one for Minecraft 1.12 to keep the players that log in.</summary>
    public static bool AppliesToServer(ModLoader loader, ReleaseVersion release)
    {
        return AppliesTo(loader, release) || loader is ModLoader.Forge && release is { Major: 1, Minor: 12 };
    }
}
