using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Modding;
using Void.Minecraft;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>The game every side of a test runs: a Minecraft release, the mod loader build on top of it, if any, and its mods.</summary>
public sealed record GameSetup(ReleaseVersion MinecraftRelease, LoaderBuild? Loader, IReadOnlyList<ModFile> Mods)
{
    public static GameSetup Vanilla(ProtocolVersion protocolVersion)
    {
        return new GameSetup(protocolVersion.FirstRelease, Loader: null, Mods: []);
    }

    public override string ToString()
    {
        return Loader is null ? $"Minecraft {MinecraftRelease}" : $"{Loader} for Minecraft {MinecraftRelease}";
    }
}
