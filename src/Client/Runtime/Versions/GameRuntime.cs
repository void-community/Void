using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

using Void.Client.Failures;

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

    /// <summary>Creates the PortableMC version of a mod loader for a Minecraft version, which installs its newest stable build when none is given.</summary>
    internal static string CreatePortableMinecraftVersion(string loader, string minecraftVersion, string? loaderVersion)
    {
        if (string.IsNullOrEmpty(loaderVersion))
            return $"{loader}:{minecraftVersion}";

        if (loader is not "forge")
            return $"{loader}:{minecraftVersion}:{loaderVersion}";

        // Forge names its builds after the Minecraft version, which modpacks and consumers usually leave out.
        return loaderVersion.StartsWith($"{minecraftVersion}-", StringComparison.Ordinal) ? $"forge::{loaderVersion}" : $"forge::{minecraftVersion}-{loaderVersion}";
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

    /// <summary>
    /// Finds a loader build in PortableMC's search output by its Minecraft version and the build as servers name it. Forge and NeoForge for
    /// Minecraft 1.20.1 add the Minecraft version in front, and old Forge builds add it again after, such as 1.7.10-10.13.4.1614-1.7.10.
    /// </summary>
    internal static string? FindPortableMinecraftLoaderVersion(IEnumerable<string[]> searchOutputFields, string minecraftVersion, string loaderVersion)
    {
        return searchOutputFields
            .Where(fields => fields is ["row", _, var gameVersion, ..] && gameVersion == minecraftVersion)
            .Select(static fields => fields[1])
            .FirstOrDefault(
                version => version == loaderVersion
                                       || version == $"{minecraftVersion}-{loaderVersion}"
                                       || version.StartsWith($"{minecraftVersion}-{loaderVersion}-", StringComparison.Ordinal)
            );
    }

    internal static bool IsStableNeoForgeVersion(string neoForgeVersion)
    {
        return !neoForgeVersion.EndsWith(value: "-beta", StringComparison.Ordinal) && !neoForgeVersion.Contains(value: "-alpha", StringComparison.Ordinal);
    }

    /// <summary>Reads what PortableMC installed from the last version hierarchy it loaded, which lists the installed version first and Minecraft last.</summary>
    internal static PortableMinecraftInstallation? ParseLoadedHierarchy(IEnumerable<string[]> outputFields)
    {
        string[]? hierarchy = outputFields.LastOrDefault(static fields => fields.Length > 1 && fields[0] == "loaded_hierarchy");

        return hierarchy is null || string.IsNullOrWhiteSpace(hierarchy[1]) || string.IsNullOrWhiteSpace(hierarchy[^1])
            ? null
            : new PortableMinecraftInstallation(hierarchy[1], hierarchy[^1]);
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
        HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(seconds: 30) };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UpstreamUserAgent);

        return httpClient;
    }

    private static async Task<IReadOnlyList<string>> GetLegacyNeoForgeVersionsAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        try
        {
            string metadataXml = await GetStringWithRetriesAsync(httpClient, new Uri(LegacyNeoForgeMavenMetadataUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ParseMavenMetadataVersions(metadataXml);
        }
        catch (Exception exception) when (exception is XmlException || IsTransientUpstreamFailure(exception, cancellationToken))
        {
            await Console.Error.WriteLineAsync($"Legacy NeoForge metadata is unavailable; Minecraft 1.20.1 cannot be selected: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);

            return [];
        }
    }

    private static async Task<string> GetStringWithRetriesAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= UpstreamRequestAttempts; attempt++)
        {
            try
            {
                return await httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
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

    private static GameClientException MinecraftPreparationFailure(string message, Exception? innerException = null, bool unavailable = false)
    {
        string code = unavailable ? "client.install.unavailable" : "client.install.failed";

        return new GameClientException(code, operation: "install", stage: "minecraft", message, innerException);
    }

    /// <summary>
    /// Runs a step that prepares Minecraft or its mod loader before the dry run, such as choosing the NeoForge build or the ARM64 natives,
    /// and reports its failure as an install failure, which is unavailable when an upstream stayed unavailable after its retries.
    /// </summary>
    private static async Task<TResult> PrepareMinecraftAsync<TResult>(Func<Task<TResult>> step, CancellationToken cancellationToken)
    {
        try
        {
            return await step().ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is not GameClientException && !cancellationToken.IsCancellationRequested)
        {
            throw MinecraftPreparationFailure(exception.Message, exception, IsUpstreamUnavailable(exception, cancellationToken));
        }
    }

    private static async Task<NeoForgeSelection> ResolveLatestStableNeoForgeAsync(string? maximumMinecraftVersion, CancellationToken cancellationToken)
    {
        using HttpClient httpClient = CreateUpstreamHttpClient();

        string manifestJson = await GetStringWithRetriesAsync(httpClient, new Uri(MojangVersionManifestUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        string neoForgeMetadataXml = await GetStringWithRetriesAsync(httpClient, new Uri(NeoForgeMavenMetadataUrl, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var legacyNeoForgeVersions = await GetLegacyNeoForgeVersionsAsync(httpClient, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
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

    /// <summary>Finds the name PortableMC installs a loader build as, from its Minecraft version and the build as servers name it.</summary>
    private async Task<string> ResolvePortableMinecraftLoaderVersionAsync(string loader, string minecraftVersion, string loaderVersion, CancellationToken cancellationToken)
    {
        var searchResult = await SearchAsync().ConfigureAwait(continueOnCapturedContext: false);

        // The search reads the loader's repository, which fails now and then.
        for (int attempt = 2; attempt <= UpstreamRequestAttempts && searchResult.ExitCode is not 0; attempt++)
            searchResult = await SearchAsync().ConfigureAwait(continueOnCapturedContext: false);

        if (searchResult.ExitCode is not 0)
        {
            throw MinecraftPreparationFailure(
                $"{loader} {loaderVersion} could not be searched for (exit code {searchResult.ExitCode}): {searchResult.StandardError.Trim()}",
                unavailable: true
            );
        }

        IEnumerable<string[]> outputFields = searchResult.StandardOutput
            .Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd(trimChar: '\r').Split(separator: '\t'));

        return FindPortableMinecraftLoaderVersion(outputFields, minecraftVersion, loaderVersion)
               ?? throw MinecraftPreparationFailure($"{loader} has no build {loaderVersion} for Minecraft {minecraftVersion}");

        Task<ProcessTextResult> SearchAsync()
        {
            return RunProcessTextAsync(
                CreateProcessStartInformation(PortableMinecraftPath, ["--output", "machine", "search", "--kind", loader, loaderVersion]),
                TimeSpan.FromMinutes(minutes: 1),
                cancellationToken
            );
        }
    }

    internal sealed record MojangVersion(string Id, string Type, Uri MetadataUri);

    internal sealed record MojangVersionManifest(string LatestRelease, string LatestSnapshot, IReadOnlyList<MojangVersion> Versions);

    internal sealed record NeoForgeSelection(string MinecraftVersion, string NeoForgeVersion, IReadOnlyList<string> SkippedMinecraftVersions);

    /// <summary>A version PortableMC installed, by the id it starts as and the Minecraft version it is built on.</summary>
    internal sealed record PortableMinecraftInstallation(string Version, string MinecraftVersion);
}
