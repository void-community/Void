using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Void.Client.Failures;
using Void.Client.Models;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    /// <summary>
    /// Connects to the agent's control port on the IPv4 loopback, falling back to the IPv6 loopback where the agent may be the only listener.
    /// </summary>
    internal static async Task<TcpClient> ConnectToAgentAsync(int port, CancellationToken cancellationToken)
    {
        TcpClient ipv4Client = new(AddressFamily.InterNetwork);

        try
        {
            await ipv4Client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ipv4Client;
        }
        catch (SocketException)
        {
            ipv4Client.Dispose();
        }
        catch
        {
            ipv4Client.Dispose();

            throw;
        }

        TcpClient ipv6Client = new(AddressFamily.InterNetworkV6);

        try
        {
            await ipv6Client.ConnectAsync(IPAddress.IPv6Loopback, port, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return ipv6Client;
        }
        catch
        {
            ipv6Client.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Copies <paramref name="source" /> to <paramref name="destination" /> and returns the SHA-1 of the copied bytes as hexadecimal.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "CurseForge publishes SHA-1 hashes for file integrity checks"
    )]
    internal static async Task<string> CopyWithSha1Async(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        byte[] buffer = new byte[81920];
        int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        while (read > 0)
        {
            hash.AppendData(buffer, offset: 0, read);
            await destination.WriteAsync(buffer.AsMemory(start: 0, read), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Lists where a CurseForge file can be downloaded: the API's download URL when the author allows third-party downloads,
    /// then CurseForge's CDN and website, which also serve files whose authors disallow API distribution.
    /// </summary>
    internal static IReadOnlyList<Uri> CreateCurseForgeDownloadUris(int modId, int fileId, string fileName, string? downloadUrl)
    {
        List<Uri> candidates = [];

        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            string escapedDownloadUrl = downloadUrl.Replace(oldValue: "[", newValue: "%5B", StringComparison.Ordinal).Replace(oldValue: "]", newValue: "%5D", StringComparison.Ordinal);

            if (Uri.TryCreate(escapedDownloadUrl, UriKind.Absolute, out var metadataUri))
                candidates.Add(metadataUri);
        }

        string fileIdText = fileId.ToString(CultureInfo.InvariantCulture);

        // The CDN stores files with ids of seven or more digits under the first four digits and the remaining digits.
        if (fileIdText.Length >= 7)
            candidates.Add(new Uri($"{CurseForgeCdnUrl}/files/{fileIdText[..4]}/{fileIdText[4..]}/{Uri.EscapeDataString(fileName)}", UriKind.Absolute));

        candidates.Add(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"{CurseForgeWebsiteUrl}/api/v1/mods/{modId}/files/{fileId}/download"),
                UriKind.Absolute
            )
        );

        return candidates;
    }

    /// <summary>
    /// Downloads the first candidate that answers successfully with content matching the published SHA-1 to <paramref name="destinationPath" />,
    /// recording why the others were skipped. Content streams through a temporary file next to the destination and only replaces it once its
    /// hash matches, so several large files can download at once without being held in memory, and nothing partial is left behind.
    /// </summary>
    internal static async Task<CurseForgeDownloadResult> DownloadCurseForgeFileAsync(
        HttpClient httpClient,
        IReadOnlyList<Uri> candidates,
        string? expectedSha1,
        string apiKey,
        string destinationPath,
        CancellationToken cancellationToken
    )
    {
        List<string> failures = [];
        string temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.download";

        try
        {
            foreach (var candidate in candidates)
            {
                string? failure = await TryDownloadCurseForgeCandidateAsync(httpClient, candidate, expectedSha1, apiKey, temporaryPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                if (failure is null)
                {
                    File.Move(temporaryPath, destinationPath, overwrite: true);

                    return new CurseForgeDownloadResult(Downloaded: true, failures);
                }

                failures.Add(failure);
            }

            return new CurseForgeDownloadResult(Downloaded: false, failures);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Reads how many CurseForge files download at the same time, which defaults to 4 like itzg's CF_PARALLEL_DOWNLOADS.
    /// </summary>
    internal static int GetCurseForgeParallelDownloads(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return DefaultCurseForgeParallelDownloads;

        bool valid = int.TryParse(configured.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parallelDownloads) && parallelDownloads > 0;

        return valid ? parallelDownloads : throw new InvalidOperationException(message: "CURSEFORGE_PARALLEL_DOWNLOADS must be a positive integer");
    }

    internal static string? GetCurseForgeSha1(IReadOnlyList<CurseForgeFileHash>? hashes)
    {
        return hashes?.FirstOrDefault(static hash => hash.Algo == CurseForgeSha1HashAlgorithm && !string.IsNullOrWhiteSpace(hash.Value))?.Value;
    }

    /// <summary>
    /// Checks a downloaded file's SHA-1 against CurseForge's published one. This is an integrity check, not a security boundary.
    /// </summary>
    internal static bool IsExpectedSha1(string actualSha1, string? expectedSha1)
    {
        return string.IsNullOrWhiteSpace(expectedSha1) || actualSha1.Equals(expectedSha1.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tells whether a modpack file is already in place, which only its published SHA-1 can confirm.</summary>
    internal static async Task<bool> IsInstalledFileAsync(string path, string? expectedSha1, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1) || !File.Exists(path))
            return false;

        using var file = File.OpenRead(path);

        return IsExpectedSha1(await CopyWithSha1Async(file, Stream.Null, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), expectedSha1);
    }

    internal static async Task<string> PrepareCurseForgeAsync(
        HttpClient httpClient,
        string slug,
        int fileId,
        string curseForgeApiKey,
        Uri curseForgeApiBaseUri,
        int parallelDownloads,
        string minecraftDirectory,
        CancellationToken cancellationToken
    )
    {
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));

        string markerFile = Path.Combine(minecraftDirectory, CurseForgeMarkerFileName);
        string versionFile = Path.Combine(minecraftDirectory, CurseForgeVersionFileName);
        string marker = $"{slug} {fileId}";
        string existingMarker = File.Exists(markerFile) ? (await File.ReadAllTextAsync(markerFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Trim() : "";
        string portablemcVersion;

        await Console.Error.WriteLineAsync($"Starting CurseForge modpack '{slug}' file '{fileId}'").ConfigureAwait(continueOnCapturedContext: false);

        if (existingMarker == marker)
        {
            if (!File.Exists(versionFile))
                throw new InvalidOperationException($"PortableMC version cache file is missing: {versionFile}");

            portablemcVersion = (await File.ReadAllTextAsync(versionFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Trim();

            await Console.Error.WriteLineAsync($"Using existing installation in {minecraftDirectory}").ConfigureAwait(continueOnCapturedContext: false);
        }
        else
        {
            portablemcVersion = await InstallModpack(httpClient, slug, fileId, curseForgeApiKey, curseForgeApiBaseUri, parallelDownloads, minecraftDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await File.WriteAllTextAsync(versionFile, portablemcVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await File.WriteAllTextAsync(markerFile, marker, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await Console.Error.WriteLineAsync(value: "Installation marker updated").ConfigureAwait(continueOnCapturedContext: false);
        }

        return portablemcVersion;
    }

    /// <summary>
    /// Leaves only the given modpack files in the folders modpack files download to. Downloads only land at the top of those folders, and
    /// the overrides extracted afterwards bring back any subfolder the modpack has, so every subfolder goes as well.
    /// </summary>
    internal static void PruneModpackFiles(string minecraftDirectory, IReadOnlySet<string> keep)
    {
        foreach (string folder in new[] { "mods", "resourcepacks", "shaderpacks" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(minecraftDirectory, folder));

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo)
                    Directory.Delete(entry.FullName, recursive: true);
                else if (!keep.Contains(entry.FullName))
                    entry.Delete();
            }
        }
    }

    /// <summary>
    /// Removes the override files of the previous modpack that the next one doesn't have, such as configs and scripts for mods it lacks.
    /// Files in the game directory itself stay, because <c>options.txt</c> there belongs to the consumer.
    /// </summary>
    internal static void RemoveStaleOverrides(string minecraftDirectory, IEnumerable<string> previous, IReadOnlyCollection<string> current)
    {
        string directory = $"{Path.GetFullPath(minecraftDirectory)}{Path.DirectorySeparatorChar}";
        HashSet<string> kept = new(current, StringComparer.Ordinal);

        foreach (string relativePath in previous)
        {
            string path = Path.GetFullPath(Path.Combine(directory, relativePath));
            bool inSubdirectory = relativePath.Contains(value: '/', StringComparison.Ordinal) && path.StartsWith(directory, StringComparison.Ordinal);

            if (inSubdirectory && !kept.Contains(relativePath))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Picks the newest build from a Modrinth version list (newest first), including pre-releases, because older Sodium
    /// releases can target loader internals that newer NeoForge builds for the same Minecraft version no longer have.
    /// </summary>
    internal static SodiumDownload? SelectSodiumDownload(JsonElement versions)
    {
        foreach (var version in versions.EnumerateArray())
        {
            if (!version.TryGetProperty(propertyName: "files", out var files))
                continue;

            string? downloadUrl = null;

            foreach (var file in files.EnumerateArray())
            {
                bool primaryFound = file.TryGetProperty(propertyName: "primary", out var primary);
                bool uniformResourceLocatorFound = file.TryGetProperty(propertyName: "url", out var uniformResourceLocator);

                if (primaryFound && primary.GetBoolean() && uniformResourceLocatorFound)
                {
                    downloadUrl = uniformResourceLocator.GetString();

                    break;
                }
            }

            if (downloadUrl is null)
                continue;

            string versionNumber = version.TryGetProperty(propertyName: "version_number", out var number) ? number.GetString() ?? "unknown" : "unknown";
            string versionType = version.TryGetProperty(propertyName: "version_type", out var type) ? type.GetString() ?? "unknown" : "unknown";

            return new SodiumDownload(versionNumber, versionType, new Uri(downloadUrl, UriKind.Absolute));
        }

        return null;
    }

    private static async Task<string> InstallModpack(
        HttpClient httpClient,
        string slug,
        int fileId,
        string apiKey,
        Uri apiBaseUri,
        int parallelDownloads,
        string minecraftDirectory,
        CancellationToken cancellationToken
    )
    {
        CurseForgeApiClient curseForgeClient = new(httpClient, apiBaseUri, apiKey);

        await Console.Error.WriteLineAsync(value: "Resolving CurseForge project").ConfigureAwait(continueOnCapturedContext: false);
        var searchResult = await curseForgeClient.SearchModsAsync(MinecraftGameId, slug, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        var project = searchResult.FirstOrDefault(modpack => modpack.Slug == slug)
                      ?? throw new InvalidOperationException($"modpack not found: {slug}");

        await Console.Error.WriteLineAsync(value: "Downloading modpack archive").ConfigureAwait(continueOnCapturedContext: false);
        var archiveFile = await curseForgeClient.GetModFileAsync(project.Id, fileId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var archiveDownloadUris = CreateCurseForgeDownloadUris(project.Id, archiveFile.Id, archiveFile.FileName, archiveFile.DownloadUrl);
        string archivePath = Path.Combine(Path.GetTempPath(), $"void-curseforge-{project.Id}-{archiveFile.Id}-{Guid.NewGuid():N}.zip");
        var archiveDownload = await DownloadCurseForgeFileAsync(httpClient, archiveDownloadUris, GetCurseForgeSha1(archiveFile.Hashes), apiKey, archivePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (!archiveDownload.Downloaded)
            throw new InvalidOperationException($"Modpack archive {slug} file {fileId} could not be downloaded: {string.Join(separator: "; ", archiveDownload.Failures)}");

        await Console.Error.WriteLineAsync(value: "Reading modpack manifest").ConfigureAwait(continueOnCapturedContext: false);

        // The archive is removed from the temporary directory as soon as it is closed.
        using ZipArchive archive = new(
            new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 81920, FileOptions.DeleteOnClose),
            ZipArchiveMode.Read
        );

        var manifestEntry = archive.GetEntry(entryName: "manifest.json")
                            ?? throw new InvalidOperationException(message: "manifest.json not found");

        using var manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(manifestStream, CurseForgeManifestJsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                       ?? throw new InvalidOperationException(message: "failed to deserialize manifest.json");

        cancellationToken.ThrowIfCancellationRequested();

        string overridesFolder = manifest.Overrides ?? "overrides";
        List<ModpackOverride> overrides = [];

        foreach (var entry in archive.Entries)
        {
            bool entryIsOverride = entry.FullName.StartsWith(overridesFolder + "/", StringComparison.Ordinal)
                                  && entry.FullName.Length > overridesFolder.Length + 1;

            if (!entryIsOverride)
                continue;

            string relativePath = entry.FullName[(overridesFolder.Length + 1)..];
            string targetPath = Path.GetFullPath(Path.Combine(minecraftDirectory, relativePath));
            bool targetWithinMinecraftDirectory = targetPath.StartsWith(Path.GetFullPath(minecraftDirectory) + Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);

            if (!targetWithinMinecraftDirectory)
                throw new InvalidDataException($"Modpack override escapes the Minecraft directory: {entry.FullName}");

            overrides.Add(new ModpackOverride(entry, relativePath, targetPath));
        }

        List<CurseForgeFile> allFileMetadata = [];

        if (manifest.Files is { Count: > 0 })
        {
            List<int> requiredFileIds = [];

            foreach (var file in manifest.Files)
            {
                if (file.Required is false)
                    continue;

                int? resolvedFileId = file.FileId;

                if (resolvedFileId is > 0)
                    requiredFileIds.Add(resolvedFileId.Value);
            }

            if (requiredFileIds.Count > 0)
                await Console.Error.WriteLineAsync($"Resolving {requiredFileIds.Count} CurseForge files").ConfigureAwait(continueOnCapturedContext: false);

            for (int batchStart = 0; batchStart < requiredFileIds.Count; batchStart += CurseForgeFilesBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<int> batch = [.. requiredFileIds.Skip(batchStart).Take(CurseForgeFilesBatchSize)];
                var files = await curseForgeClient.GetFilesAsync(batch, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                allFileMetadata.AddRange(files);
            }
        }

        foreach (var fileMeta in allFileMetadata)
            ValidateFileName(fileMeta.FileName, fileMeta.ModId, fileMeta.Id);

        // Nothing in the game directory changed so far, so a failure above leaves the installed modpack startable. From here on, a failed
        // or stopped install must not pass as any modpack, until the caller writes the marker of this one.
        File.Delete(Path.Combine(minecraftDirectory, CurseForgeMarkerFileName));
        File.Delete(Path.Combine(minecraftDirectory, CurseForgeVersionFileName));

        // The list is written before the overrides are extracted, so the next install also removes what a failed one extracted.
        string overridesListPath = Path.Combine(minecraftDirectory, CurseForgeOverridesFileName);
        List<string> overrideFiles = [.. overrides.Where(static item => !item.Entry.FullName.EndsWith(value: '/')).Select(static item => item.RelativePath)];
        string[] previousOverrideFiles = File.Exists(overridesListPath) ? await File.ReadAllLinesAsync(overridesListPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false) : [];
        RemoveStaleOverrides(minecraftDirectory, previousOverrideFiles, overrideFiles);
        await File.WriteAllLinesAsync(overridesListPath, overrideFiles, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        PruneModpackFiles(
            minecraftDirectory,
            allFileMetadata.Select(fileMeta => Path.Combine(DetermineTargetDirectory(fileMeta.FileName, minecraftDirectory), fileMeta.FileName)).ToHashSet(StringComparer.Ordinal)
        );

        await Console.Error.WriteLineAsync($"Prepared Minecraft directory: {minecraftDirectory}").ConfigureAwait(continueOnCapturedContext: false);

        if (allFileMetadata.Count > 0)
        {
            int totalFileCount = allFileMetadata.Count;
            int downloadIndex = 0;
            ConcurrentDictionary<int, string> missingFiles = [];
            ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = parallelDownloads, CancellationToken = cancellationToken };

            await Console.Error.WriteLineAsync($"Downloading {totalFileCount} CurseForge files, {parallelDownloads} at a time").ConfigureAwait(continueOnCapturedContext: false);

            // Most of a file's download time is request latency, so a few downloads in flight finish a large pack much sooner.
            await Parallel.ForEachAsync(
                allFileMetadata.Index(),
                parallelOptions,
                async (entry, downloadCancellationToken) =>
                {
                    var (position, fileMeta) = entry;
                    int fileNumber = Interlocked.Increment(ref downloadIndex);
                    string destinationPath = Path.Combine(DetermineTargetDirectory(fileMeta.FileName, minecraftDirectory), fileMeta.FileName);
                    string? expectedSha1 = GetCurseForgeSha1(fileMeta.Hashes);

                    // An earlier install, of this modpack or another one, may have left the same file.
                    bool installed = await IsInstalledFileAsync(destinationPath, expectedSha1, downloadCancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    if (installed)
                    {
                        await Console.Error.WriteLineAsync($"[{fileNumber}/{totalFileCount}] Already exists: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                        return;
                    }

                    await Console.Error.WriteLineAsync($"[{fileNumber}/{totalFileCount}] Downloading: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                    var fileDownloadUris = CreateCurseForgeDownloadUris(fileMeta.ModId, fileMeta.Id, fileMeta.FileName, fileMeta.DownloadUrl);

                    var fileDownload = await DownloadCurseForgeFileAsync(httpClient, fileDownloadUris, expectedSha1, apiKey, destinationPath, downloadCancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    if (fileDownload.Downloaded)
                        return;

                    string missingFile = $"{fileMeta.FileName} (mod {fileMeta.ModId}, file {fileMeta.Id})";
                    missingFiles[position] = missingFile;

                    await Console.Error.WriteLineAsync($"[{fileNumber}/{totalFileCount}] Could not download {missingFile}: {string.Join(separator: "; ", fileDownload.Failures)}").ConfigureAwait(continueOnCapturedContext: false);
                }
            ).ConfigureAwait(continueOnCapturedContext: false);

            if (!missingFiles.IsEmpty)
            {
                var missingFilesInManifestOrder = missingFiles.OrderBy(static missingFile => missingFile.Key).Select(static missingFile => missingFile.Value);

                throw new InvalidOperationException($"{missingFiles.Count} CurseForge files could not be downloaded: {string.Join(separator: ", ", missingFilesInManifestOrder)}");
            }
        }
        else
        {
            await Console.Error.WriteLineAsync(value: "No CurseForge files to download").ConfigureAwait(continueOnCapturedContext: false);
        }

        // Overrides come last, so one that has the name of a downloaded file replaces it.
        if (overrides.Count > 0)
        {
            await Console.Error.WriteLineAsync(value: "Installing modpack overrides").ConfigureAwait(continueOnCapturedContext: false);

            foreach (var modpackOverride in overrides)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (modpackOverride.Entry.FullName.EndsWith(value: '/'))
                {
                    var targetDirectoryInformation = Directory.CreateDirectory(modpackOverride.TargetPath);

                    continue;
                }

                var parentDirectoryInformation = Directory.CreateDirectory(Path.GetDirectoryName(modpackOverride.TargetPath) ?? minecraftDirectory);

                using var sourceStream = await modpackOverride.Entry.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                using var targetStream = File.Create(modpackOverride.TargetPath);

                await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        await Console.Error.WriteLineAsync(value: "Resolving PortableMC version").ConfigureAwait(continueOnCapturedContext: false);

        string minecraftVersion = manifest.Minecraft?.Version
                               ?? throw new InvalidOperationException(message: "minecraft.version missing");

        string portablemcVersion = $"mojang:{minecraftVersion}";

        if (manifest.Minecraft.ModLoaders is not null)
        {
            foreach (var loader in manifest.Minecraft.ModLoaders)
            {
                if (loader.Primary != true)
                    continue;

                string loaderId = loader.Id ?? "";

                if (loaderId.StartsWith(value: "neoforge-", StringComparison.Ordinal))
                {
                    portablemcVersion = $"neoforge::{loaderId["neoforge-".Length..]}";
                }
                else if (loaderId.StartsWith(value: "forge-", StringComparison.Ordinal))
                {
                    string forgeVersion = loaderId["forge-".Length..];
                    portablemcVersion = forgeVersion.StartsWith($"{minecraftVersion}-", StringComparison.Ordinal)
                        ? $"forge::{forgeVersion}"
                        : $"forge::{minecraftVersion}-{forgeVersion}";
                }
                else
                {
                    portablemcVersion = loaderId.StartsWith(value: "fabric-", StringComparison.Ordinal)
                        ? $"fabric:{minecraftVersion}:{loaderId["fabric-".Length..]}"
                        : loaderId.StartsWith(value: "quilt-", StringComparison.Ordinal)
                        ? $"quilt:{minecraftVersion}:{loaderId["quilt-".Length..]}"
                        : throw new InvalidOperationException($"Unsupported mod loader for CurseForge modpack '{slug}' (file id: {fileId}): {loaderId}");
                }

                break;
            }
        }

        return portablemcVersion;
    }

    private static async Task InstallSodiumAsync(string modsDirectory, string portableMinecraftVersion, string minecraftVersion, CancellationToken cancellationToken)
    {
        string sodiumPath = Path.Combine(modsDirectory, path2: "sodium.jar");
        string temporarySodiumPath = Path.Combine(modsDirectory, $".sodium.jar.{Guid.NewGuid():N}");
        int separatorIndex = portableMinecraftVersion.IndexOf(value: ':', StringComparison.Ordinal);
        string loader = separatorIndex < 0 ? "mojang" : portableMinecraftVersion[..separatorIndex];
        string loaders = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }));
        string gameVersions = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }));

        File.Delete(sodiumPath);

        try
        {
            using HttpClient httpClient = new();

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(input: "caunt/Void");

            Uri versionsUri = new($"https://api.modrinth.com/v2/project/AANobbMI/version?loaders={loaders}&game_versions={gameVersions}", UriKind.Absolute);

            using var versionsResponse = await httpClient.GetAsync(versionsUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulVersionsResponse = versionsResponse.EnsureSuccessStatusCode();

            using var versionsStream = await successfulVersionsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (SelectSodiumDownload(versionsDocument.RootElement) is not { } sodiumDownload)
            {
                await Console.Error.WriteLineAsync($"Sodium was not found for {loader} Minecraft {minecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            using var sodiumResponse = await httpClient.GetAsync(sodiumDownload.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulSodiumResponse = sodiumResponse.EnsureSuccessStatusCode();

            using (var sodiumFile = File.Create(temporarySodiumPath))
                await successfulSodiumResponse.Content.CopyToAsync(sodiumFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            File.Move(temporarySodiumPath, sodiumPath, overwrite: true);

            await Console.Error.WriteLineAsync($"Installed Sodium {sodiumDownload.VersionNumber} ({sodiumDownload.VersionType}) for {loader} Minecraft {minecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            await Console.Error.WriteLineAsync($"Sodium download failed for {loader} Minecraft {minecraftVersion}: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync($"Sodium download failed for {loader} Minecraft {minecraftVersion}: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            File.Delete(temporarySodiumPath);
        }
    }

    private static bool IsProcessGroupRunning(int processGroupId)
    {
        foreach (string processDirectory in Directory.EnumerateDirectories(path: "/proc"))
        {
            try
            {
                string status = File.ReadAllText(Path.Combine(processDirectory, path2: "stat"));
                int commandEnd = status.LastIndexOf(value: ')');

                if (commandEnd < 0)
                    continue;

                string[] fields = status[(commandEnd + 1)..].Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);
                int candidateProcessGroupId = 0;

                bool candidateParsed = fields.Length > 2
                                      && fields[0] is not "Z"
                                      && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out candidateProcessGroupId);

                if (candidateParsed && candidateProcessGroupId == processGroupId)
                    return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Process state changed during process-group inspection: {exception.Message}");
            }
        }

        return false;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Failed to kill process {process.ProcessName}: {exception}");
        }
    }

    private static GamePlayersException PlayersUnavailable(string message, string stage = "snapshot", Exception? innerException = null)
    {
        return new(StatusCodes.Status503ServiceUnavailable, code: "client.players.unavailable", stage, message, innerException);
    }

    private static async Task<ProcessBytesResult> RunProcessBytesAsync(ProcessStartInfo processStartInformation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var process = Process.Start(processStartInformation)
                      ?? throw new InvalidOperationException($"failed to start {processStartInformation.FileName}");

        MemoryStream standardOutput = new();

        var standardOutputTask = process.StandardOutput.BaseStream.CopyToAsync(standardOutput, cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await WaitForProcessExitAsync(process, processStartInformation.FileName, timeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await standardOutputTask.ConfigureAwait(continueOnCapturedContext: false);

            return new ProcessBytesResult(process.ExitCode, standardOutput.ToArray(), await standardErrorTask.ConfigureAwait(continueOnCapturedContext: false));
        }
        finally
        {
            await standardOutputTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await ((Task)standardErrorTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await standardOutput.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
            process.Dispose();
        }
    }

    private static async Task TryCancelAgentCommandAsync(GameTrackerConnection tracker, int port, string requestId)
    {
        try
        {
            using TcpClient client = await ConnectToAgentAsync(port, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync($"{tracker.Token}\tcancel\t{requestId}".AsMemory(), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or InvalidOperationException or ObjectDisposedException)
        {
            await Console.Error.WriteLineAsync($"Minecraft agent command cancellation was unavailable: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    /// <summary>
    /// Streams one candidate to <paramref name="temporaryPath" /> and checks it against the published SHA-1.
    /// </summary>
    /// <returns><see langword="null" /> when the file matches; otherwise, why the candidate was skipped.</returns>
    private static async Task<string?> TryDownloadCurseForgeCandidateAsync(
        HttpClient httpClient,
        Uri candidate,
        string? expectedSha1,
        string apiKey,
        string temporaryPath,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, candidate);

        if (candidate.Host.EndsWith(value: "curseforge.com", StringComparison.OrdinalIgnoreCase))
            request.Headers.Add(name: "x-api-key", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (!response.IsSuccessStatusCode)
                return $"{candidate.Host} returned {(int)response.StatusCode}";

            string actualSha1;
            var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await using (source.ConfigureAwait(continueOnCapturedContext: false))
            {
                FileStream target = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

                await using (target.ConfigureAwait(continueOnCapturedContext: false))
                    actualSha1 = await CopyWithSha1Async(source, target, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }

            return IsExpectedSha1(actualSha1, expectedSha1) ? null : $"{candidate.Host} served content that does not match SHA-1 {expectedSha1}";
        }
        catch (HttpRequestException exception)
        {
            return $"{candidate.Host} failed: {exception.Message}";
        }
        catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return $"{candidate.Host} failed: {exception.Message}";
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return $"{candidate.Host} timed out: {exception.Message}";
        }
    }

    private static bool TryReadParentProcessId(string processDirectory, out int parentProcessId)
    {
        parentProcessId = 0;

        try
        {
            foreach (string line in File.ReadLines(Path.Combine(processDirectory, path2: "status")))
            {
                if (!line.StartsWith(value: "PPid:", StringComparison.Ordinal))
                    continue;

                return int.TryParse(line.AsSpan("PPid:".Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parentProcessId);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Process ended before its parent id could be read: {exception.Message}");
        }

        return false;
    }

    private static void ValidateFileName(string fileName, int modId, int modFileId)
    {
        string safeName = Path.GetFileName(fileName);

        bool fileNameIsSafe = safeName == fileName
                             && !string.IsNullOrEmpty(safeName)
                             && safeName is not "." and not ".."
                             && !safeName.Contains(value: '/', StringComparison.Ordinal)
                             && !safeName.Contains(value: '\\', StringComparison.Ordinal)
                             && !safeName.Any(char.IsControl);

        if (!fileNameIsSafe)
            throw new InvalidOperationException($"Unexpected CurseForge file name '{fileName}' for mod id '{modId}' and file id '{modFileId}'");
    }

    private static void ValidatePlayer(GamePlayer player)
    {
        bool positionIsFinite = double.IsFinite(player.Position.XCoordinate)
                               && double.IsFinite(player.Position.YCoordinate)
                               && double.IsFinite(player.Position.ZCoordinate);

        if (!positionIsFinite)
            throw PlayersUnavailable(message: "The Minecraft player tracker returned a non-finite player coordinate");

        ValidateRotation(player.Body, player.Head);
    }

    private static void ValidatePlayer(RemoteGamePlayer player)
    {
        bool positionIsFinite = double.IsFinite(player.Position.XCoordinate)
                               && double.IsFinite(player.Position.YCoordinate)
                               && double.IsFinite(player.Position.ZCoordinate);

        if (!positionIsFinite)
            throw PlayersUnavailable(message: "The Minecraft player tracker returned a non-finite player coordinate");

        ValidateRotation(player.Body, player.Head);
    }

    private static void ValidateRotation(BodyRotation? body, HeadRotation? head)
    {
        if (body is null || head is null)
            throw PlayersUnavailable(message: "The Minecraft player tracker returned an incomplete player rotation");

        if (!double.IsFinite(body.Yaw) || !double.IsFinite(head.Yaw) || !double.IsFinite(head.Pitch))
            throw PlayersUnavailable(message: "The Minecraft player tracker returned a non-finite player rotation");
    }

    private static async Task WaitForKilledProcessAsync(Process process)
    {
        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        await waitTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private static async Task WaitForProcessExitAsync(Process process, string processName, TimeSpan timeout, CancellationToken cancellationToken, bool killOnTimeout = true)
    {
        using CancellationTokenSource timeoutSource = new(timeout);

        using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedCancellationTokenSource.Token).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (killOnTimeout)
            {
                KillProcess(process);
                await WaitForKilledProcessAsync(process).ConfigureAwait(continueOnCapturedContext: false);
            }

            throw new TimeoutException($"{processName} timed out after {timeout.TotalSeconds:F1} seconds");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            await WaitForKilledProcessAsync(process).ConfigureAwait(continueOnCapturedContext: false);

            throw;
        }
    }

    private static async Task WaitForProcessGroupExitAsync(int processGroupId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (IsProcessGroupRunning(processGroupId))
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException($"Minecraft process group did not exit within {timeout.TotalSeconds:F1} seconds");

            await Task.Delay(millisecondsDelay: 50, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    internal sealed record CurseForgeDownloadResult(bool Downloaded, IReadOnlyList<string> Failures);

    internal sealed record SodiumDownload(string VersionNumber, string VersionType, Uri DownloadUri);
}
