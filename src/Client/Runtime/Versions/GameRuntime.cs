using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    internal static string? CreateNeoForgeVersionPrefix(string minecraftVersion)
    {
        return ParseMinecraftReleaseComponents(minecraftVersion) is not [var majorVersion, var minorVersion, var patchVersion]
            ? null
            : majorVersion >= 26
            ? string.Create(CultureInfo.InvariantCulture, $"{majorVersion}.{minorVersion}.{patchVersion}.")
            : majorVersion == 20 && minorVersion == 1
            ? "1.20.1-"
            : string.Create(CultureInfo.InvariantCulture, $"{majorVersion}.{minorVersion}.");
    }

    internal static string? FindLatestStableNeoForgeVersion(IEnumerable<string> neoForgeVersions, string minecraftVersion)
    {
        string? prefix = CreateNeoForgeVersionPrefix(minecraftVersion);

        if (prefix is null)
            return null;

        ushort[] latestComponents = [0, 0, 0, 0];
        string? latestVersion = null;

        foreach (string neoForgeVersion in neoForgeVersions)
        {
            if (!neoForgeVersion.StartsWith(prefix, StringComparison.Ordinal) || !IsStableNeoForgeVersion(neoForgeVersion))
                continue;

            ushort[]? components = ParseVersionComponents(neoForgeVersion[prefix.Length..], maximumCount: 4, minimumCount: 1, ignoreSuffix: true);

            if (components is null)
                continue;

            if (CompareVersionComponents(components, latestComponents) <= 0)
                continue;

            latestComponents = components;
            latestVersion = neoForgeVersion;
        }

        return latestVersion;
    }

    internal static bool IsStableNeoForgeVersion(string neoForgeVersion)
    {
        return !neoForgeVersion.EndsWith(value: "-beta", StringComparison.Ordinal) && !neoForgeVersion.Contains(value: "-alpha", StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> ParseMavenMetadataVersions(string metadataXml)
    {
        return [.. XDocument.Parse(metadataXml).Descendants(name: "version").Select(static element => element.Value.Trim()).Where(static version => version.Length > 0)];
    }

    internal static ushort[]? ParseMinecraftReleaseComponents(string minecraftVersion)
    {
        if (minecraftVersion.StartsWith(value: "1.", StringComparison.Ordinal))
        {
            string legacyVersion = minecraftVersion[2..];

            bool legacyVersionIsPreview = legacyVersion.Contains(value: "-pre", StringComparison.Ordinal)
                                         || legacyVersion.Contains(value: "-rc", StringComparison.Ordinal)
                                         || legacyVersion.Contains(value: " Pre-Release ", StringComparison.Ordinal);

            return legacyVersionIsPreview ? null : ParseVersionComponents(legacyVersion, maximumCount: 3, minimumCount: 1, ignoreSuffix: false);
        }

        bool versionIsPreview = minecraftVersion.Contains(value: "-pre", StringComparison.Ordinal)
                               || minecraftVersion.Contains(value: "-rc", StringComparison.Ordinal)
                               || minecraftVersion.Contains(value: "-snapshot", StringComparison.Ordinal);

        return versionIsPreview ? null : ParseVersionComponents(minecraftVersion, maximumCount: 3, minimumCount: 2, ignoreSuffix: false);
    }

    internal static MojangVersionManifest ParseMojangVersionManifest(string manifestJson)
    {
        using JsonDocument document = JsonDocument.Parse(manifestJson);

        var root = document.RootElement;
        var latest = root.GetProperty(propertyName: "latest");
        List<MojangVersion> versions = [];

        foreach (var version in root.GetProperty(propertyName: "versions").EnumerateArray())
        {
            string? id = version.GetProperty(propertyName: "id").GetString();
            string? type = version.GetProperty(propertyName: "type").GetString();
            string? url = version.GetProperty(propertyName: "url").GetString();

            if (id is null || type is null || url is null)
                continue;

            versions.Add(new MojangVersion(id, type, new Uri(url, UriKind.Absolute)));
        }

        return new MojangVersionManifest(
            latest.GetProperty(propertyName: "release").GetString() ?? "",
            latest.GetProperty(propertyName: "snapshot").GetString() ?? "",
            versions
        );
    }

    internal static ushort[]? ParseVersionComponents(string version, int maximumCount, int minimumCount, bool ignoreSuffix)
    {
        if (ignoreSuffix && version.IndexOf(value: '-', StringComparison.Ordinal) is var suffixIndex and >= 0)
            version = version[..suffixIndex];

        string[] parts = version.Split(separator: '.');
        ushort[] components = new ushort[maximumCount];

        for (int index = 0; index < maximumCount; index++)
        {
            if (index >= parts.Length)
            {
                if (index < minimumCount)
                    return null;

                continue;
            }

            if (!ushort.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out ushort component))
                return null;

            components[index] = component;
        }

        return components;
    }

    internal static NeoForgeSelection SelectLatestStableNeoForge(IReadOnlyList<string> releaseIdsNewestFirst, IReadOnlyCollection<string> neoForgeVersions, string? maximumMinecraftVersion)
    {
        int startIndex = 0;

        if (maximumMinecraftVersion is not null)
        {
            startIndex = releaseIdsNewestFirst.ToList().IndexOf(maximumMinecraftVersion);

            if (startIndex < 0)
                throw new InvalidOperationException($"maximumMinecraftVersion {maximumMinecraftVersion} is not a Mojang release");
        }

        if (startIndex >= releaseIdsNewestFirst.Count)
            throw new InvalidOperationException(message: "The Mojang version manifest lists no releases");

        List<string> skippedMinecraftVersions = [];

        for (int index = startIndex; index < releaseIdsNewestFirst.Count; index++)
        {
            string minecraftVersion = releaseIdsNewestFirst[index];

            if (FindLatestStableNeoForgeVersion(neoForgeVersions, minecraftVersion) is { } neoForgeVersion)
                return new NeoForgeSelection(minecraftVersion, neoForgeVersion, skippedMinecraftVersions);

            skippedMinecraftVersions.Add(minecraftVersion);
        }

        throw new InvalidOperationException($"No stable NeoForge build exists for Minecraft {releaseIdsNewestFirst[startIndex]} or any earlier release");
    }

    private static int CompareVersionComponents(ushort[] leftComponents, ushort[] rightComponents)
    {
        for (int index = 0; index < Math.Min(leftComponents.Length, rightComponents.Length); index++)
        {
            int comparison = leftComponents[index].CompareTo(rightComponents[index]);

            if (comparison != 0)
                return comparison;
        }

        return leftComponents.Length.CompareTo(rightComponents.Length);
    }

    private static HttpClient CreateUpstreamHttpClient()
    {
        HttpClient hypertextTransferProtocolClient = new() { Timeout = TimeSpan.FromSeconds(seconds: 30) };

        hypertextTransferProtocolClient.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamUserAgent);

        return hypertextTransferProtocolClient;
    }

    private static async Task<IReadOnlyList<string>> GetLegacyNeoForgeVersionsAsync(HttpClient hypertextTransferProtocolClient, CancellationToken cancellationToken)
    {
        try
        {
            string metadataXml = await GetStringWithRetriesAsync(hypertextTransferProtocolClient, new Uri(LegacyNeoForgeMavenMetadataUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ParseMavenMetadataVersions(metadataXml);
        }
        catch (Exception exception) when (exception is XmlException || IsTransientUpstreamFailure(exception, cancellationToken))
        {
            await Console.Error.WriteLineAsync($"Legacy NeoForge metadata is unavailable; Minecraft 1.20.1 cannot be selected: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);

            return [];
        }
    }

    private static async Task<string> GetStringWithRetriesAsync(HttpClient hypertextTransferProtocolClient, Uri uri, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= UpstreamRequestAttempts; attempt++)
        {
            try
            {
                return await hypertextTransferProtocolClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception) when (attempt < UpstreamRequestAttempts && IsTransientUpstreamFailure(exception, cancellationToken))
            {
                await Console.Error.WriteLineAsync($"Request to {uri} failed on attempt {attempt}/{UpstreamRequestAttempts}: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        throw new InvalidOperationException($"Request attempts were exhausted for {uri}");
    }

    private static bool IsTransientUpstreamFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private static async Task<NeoForgeSelection> ResolveLatestStableNeoForgeAsync(string? maximumMinecraftVersion, CancellationToken cancellationToken)
    {
        using HttpClient hypertextTransferProtocolClient = CreateUpstreamHttpClient();

        string manifestJson = await GetStringWithRetriesAsync(hypertextTransferProtocolClient, new Uri(MojangVersionManifestUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string neoForgeMetadataXml = await GetStringWithRetriesAsync(hypertextTransferProtocolClient, new Uri(NeoForgeMavenMetadataUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var legacyNeoForgeVersions = await GetLegacyNeoForgeVersionsAsync(hypertextTransferProtocolClient, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var manifest = ParseMojangVersionManifest(manifestJson);
        IReadOnlyList<string> neoForgeVersions = [.. ParseMavenMetadataVersions(neoForgeMetadataXml), .. legacyNeoForgeVersions];

        IReadOnlyList<string> releaseIds = [.. manifest.Versions.Where(static version => version.Type is "release").Select(static version => version.Id)];

        var selection = SelectLatestStableNeoForge(releaseIds, neoForgeVersions, maximumMinecraftVersion);
        string skippedMinecraftVersions = selection.SkippedMinecraftVersions.Count > 0 ? string.Join(separator: ", ", selection.SkippedMinecraftVersions) : "none";

        await Console.Error.WriteLineAsync(
            $"Resolved NeoForge {selection.NeoForgeVersion} for Minecraft {selection.MinecraftVersion} (latest release {manifest.LatestRelease}, maximum {maximumMinecraftVersion ?? "none"}, skipped without stable NeoForge: {skippedMinecraftVersions})"
        ).ConfigureAwait(continueOnCapturedContext: false);

        return selection;
    }

    internal sealed record MojangVersion(string Id, string Type, Uri MetadataUri);

    internal sealed record MojangVersionManifest(string LatestRelease, string LatestSnapshot, IReadOnlyList<MojangVersion> Versions);

    internal sealed record NeoForgeSelection(string MinecraftVersion, string NeoForgeVersion, IReadOnlyList<string> SkippedMinecraftVersions);
}
