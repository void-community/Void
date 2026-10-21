using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Void.Minecraft;

namespace Void.IntegrationTests.Infrastructure.Modding;

/// <summary>Finds the Java version a Minecraft release runs on, as Mojang's launcher does.</summary>
internal static class MojangJavaVersionCatalog
{
    // Releases from before Mojang listed a Java version ran on Java 8.
    private const int DefaultJavaMajorVersion = 8;
    private static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private static readonly AsyncLazy<IReadOnlyDictionary<string, Uri>> VersionUris = new(LoadVersionUrisAsync, AsyncLazyFlags.RetryOnFailure);
    private static readonly ConcurrentDictionary<ReleaseVersion, AsyncLazy<int>> JavaVersionLookups = new();

    public static Task<int> FindJavaMajorVersionAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        return JavaVersionLookups.GetOrAdd(release, _ => new AsyncLazy<int>(() => LoadJavaMajorVersionAsync(release), AsyncLazyFlags.RetryOnFailure)).Task.WaitAsync(cancellationToken);
    }

    private static async Task<int> LoadJavaMajorVersionAsync(ReleaseVersion release)
    {
        var versionUris = await VersionUris;

        if (!versionUris.TryGetValue(release.ToString(), out var versionUri))
            throw new InvalidOperationException($"Mojang's version manifest has no Minecraft {release}");

        using var version = JsonDocument.Parse(await UpstreamHttp.GetStringAsync(versionUri, CancellationToken.None));
        var hasJavaVersion = version.RootElement.TryGetProperty("javaVersion", out var javaVersion);

        return hasJavaVersion ? javaVersion.GetProperty("majorVersion").GetInt32() : DefaultJavaMajorVersion;
    }

    private static async Task<IReadOnlyDictionary<string, Uri>> LoadVersionUrisAsync()
    {
        using var manifest = JsonDocument.Parse(await UpstreamHttp.GetStringAsync(ManifestUri, CancellationToken.None));

        return manifest.RootElement.GetProperty("versions").EnumerateArray().ToDictionary(
            version => version.GetProperty("id").GetString() ?? "",
            version => new Uri(version.GetProperty("url").GetString() ?? "")
        );
    }
}
