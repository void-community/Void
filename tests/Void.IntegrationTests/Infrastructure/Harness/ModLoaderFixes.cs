using System;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/ModLoaderFixes, which fixes the bugs of Forge, NeoForge and Minecraft itself that games and servers
/// hit while they start, log players in or move between servers, such as Forge for Minecraft 1.7.2 not starting on Java 8u20 and later. It
/// leaves a game alone when the game has none of the code a fix targets, so every game and every server plays with it.
/// </summary>
internal static class ModLoaderFixes
{
    /// <summary>The Java agent of the latest release, a jar that loads with <c>-javaagent</c> on every architecture.</summary>
    public static Uri Url { get; } = new("https://github.com/void-community/ModLoaderFixes/releases/latest/download/modloaderfixes.jar");
}
