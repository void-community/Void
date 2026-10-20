using System.Collections.Generic;
using Void.IntegrationTests.Infrastructure.Harness;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>
/// The modded games a protocol version can run, in the order to try them, or why it runs none: either the loader or mods have not released
/// for it yet, which is worth a warning, or they moved past it long ago, which is not.
/// </summary>
public sealed record ModdedGameResolution(IReadOnlyList<GameSetup> Candidates, string SkipReason, bool WarnOnSkip)
{
    public static ModdedGameResolution Runnable(IReadOnlyList<GameSetup> candidates)
    {
        return new ModdedGameResolution(candidates, SkipReason: "", WarnOnSkip: false);
    }

    public static ModdedGameResolution Skipped(string reason, bool notReleasedYet)
    {
        return new ModdedGameResolution(Candidates: [], reason, notReleasedYet);
    }
}
