using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// The JVM agent from void-community/LegacyForgePatcher, without which Forge for Minecraft 1.7.2 stops with a ConcurrentModificationException
/// on Java 8u20 and later, because its FML sorts LaunchWrapper's tweakers while LaunchWrapper iterates them.
/// </summary>
internal static class LegacyForgePatcher
{
    public const string ContainerPath = "/opt/legacyforgepatcher/legacyforgepatcher.so";

    private static readonly ReleaseVersion PatchedRelease = new(Major: 1, Minor: 7, Patch: 2);
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "void-community/Void integration-tests" } },
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static readonly AsyncLazy<byte[]> Agent = new(DownloadAsync, AsyncLazyFlags.RetryOnFailure);

    /// <summary>The argument that loads the agent into a JVM whose container has it at <see cref="ContainerPath"/>.</summary>
    public static string JvmArgument => $"-agentpath:{ContainerPath}";

    public static bool AppliesTo(ModLoader loader, ReleaseVersion release)
    {
        return loader is ModLoader.Forge && release == PatchedRelease;
    }

    public static Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        return Agent.Task.WaitAsync(cancellationToken);
    }

    private static async Task<byte[]> DownloadAsync()
    {
        // Containers run the architecture of the Docker host, which runs the tests too.
        var platform = RuntimeInformation.OSArchitecture is Architecture.Arm64 ? "linux-arm64" : "linux-x64";

        return await HttpClient.GetByteArrayAsync($"https://github.com/void-community/LegacyForgePatcher/releases/latest/download/legacyforgepatcher-{platform}.so");
    }
}
