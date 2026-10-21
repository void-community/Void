using System.Collections.Generic;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The modded game every side of a test runs: a mod loader build, by the version servers report it as, on top of a Minecraft release, and
/// its mods.
/// </summary>
public sealed record GameSetup(ModLoader Loader, string LoaderVersion, ReleaseVersion MinecraftRelease, IReadOnlyList<ModFile> Mods)
{
    public override string ToString()
    {
        return $"{Loader} {LoaderVersion} for Minecraft {MinecraftRelease}";
    }
}
