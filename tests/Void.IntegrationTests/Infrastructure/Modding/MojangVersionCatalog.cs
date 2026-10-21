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

/// <summary>Reads what Mojang's launcher knows about a Minecraft release: when it came out, and the Java version it runs on.</summary>
internal static class MojangVersionCatalog
{
    // Releases from before Mojang listed a Java version ran on Java 8.
    private const int DefaultJavaMajorVersion = 8;
    private static readonly Uri ManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
    private static readonly AsyncLazy<IReadOnlyDictionary<string, MojangVersion>> Versions = new(LoadVersionsAsync, AsyncLazyFlags.RetryOnFailure);
    private static readonly ConcurrentDictionary<ReleaseVersion, AsyncLazy<int>> JavaVersionLookups = new();

    public static Task<int> FindJavaMajorVersionAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        return JavaVersionLookups.GetOrAdd(release, _ => new AsyncLazy<int>(() => LoadJavaMajorVersionAsync(release), AsyncLazyFlags.RetryOnFailure)).Task.WaitAsync(cancellationToken);
    }

    public static async Task<DateTimeOffset> FindReleaseTimeAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        return (await FindVersionAsync(release, cancellationToken)).ReleaseTime;
    }

    private static async Task<MojangVersion> FindVersionAsync(ReleaseVersion release, CancellationToken cancellationToken)
    {
        var versions = await Versions.Task.WaitAsync(cancellationToken);

        return versions.TryGetValue(release.ToString(), out var version) ? version : throw new InvalidOperationException($"Mojang's version manifest has no Minecraft {release}");
    }

    private static async Task<int> LoadJavaMajorVersionAsync(ReleaseVersion release)
    {
        var version = await FindVersionAsync(release, CancellationToken.None);

        using var metadata = JsonDocument.Parse(await UpstreamHttp.GetStringAsync(version.MetadataUri, CancellationToken.None));
        var hasJavaVersion = metadata.RootElement.TryGetProperty("javaVersion", out var javaVersion);

        return hasJavaVersion ? javaVersion.GetProperty("majorVersion").GetInt32() : DefaultJavaMajorVersion;
    }

    private static async Task<IReadOnlyDictionary<string, MojangVersion>> LoadVersionsAsync()
    {
        using var manifest = JsonDocument.Parse(await UpstreamHttp.GetStringAsync(ManifestUri, CancellationToken.None));

        return manifest.RootElement.GetProperty("versions").EnumerateArray().ToDictionary(
            version => version.GetProperty("id").GetString() ?? "",
            version => new MojangVersion(new Uri(version.GetProperty("url").GetString() ?? ""), version.GetProperty("releaseTime").GetDateTimeOffset())
        );
    }

    private sealed record MojangVersion(Uri MetadataUri, DateTimeOffset ReleaseTime);
}
