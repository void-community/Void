using System;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/LegacyForgePatcher, which fixes the bugs of Forge, NeoForge and Minecraft itself that games and servers
/// hit while they start, log players in or move between servers, such as Forge for Minecraft 1.7.2 not starting on Java 8u20 and later. It
/// leaves a game alone when the game has none of the code a fix targets, so every game plays with it, and every modded server that can
/// load it.
/// </summary>
internal static class LegacyForgePatcher
{
    /// <summary>Where servers keep the agent.</summary>
    public const string ServerPath = "/opt/legacyforgepatcher/legacyforgepatcher.jar";

    /// <summary>The Java agent of the latest release, a jar that loads with <c>-javaagent</c> on every architecture.</summary>
    public static Uri Url { get; } = new("https://github.com/void-community/LegacyForgePatcher/releases/latest/download/legacyforgepatcher.jar");

    /// <summary>Whether a server image can load the agent, which needs glibc 2.34 or later, newer than the image's Java 17 variant has.</summary>
    public static bool LoadsOn(int javaMajorVersion)
    {
        return javaMajorVersion is not 17;
    }
}
