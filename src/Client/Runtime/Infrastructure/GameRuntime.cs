using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Void.Client.Failures;
using Void.Client.Models;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private static async Task<string> InstallModpack(string slug, int fileId, string apiKey, Uri apiBaseUri, string minecraftDirectory, CancellationToken cancellationToken)
    {
        using HttpClient hypertextTransferProtocolClient = new();

        CurseForgeApiClient curseForgeClient = new(hypertextTransferProtocolClient, apiBaseUri, apiKey);

        await Console.Error.WriteLineAsync(value: "Resolving CurseForge project").ConfigureAwait(continueOnCapturedContext: false);
        var searchResult = await curseForgeClient.SearchModsAsync(MinecraftGameId, slug, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        var project = searchResult.FirstOrDefault(modpack => modpack.Slug == slug)
                      ?? throw new InvalidOperationException($"modpack not found: {slug}");

        await Console.Error.WriteLineAsync(value: "Downloading modpack archive").ConfigureAwait(continueOnCapturedContext: false);
        string archiveDownloadUrl = await ResolveDownloadUrlAsync(curseForgeClient, project.Id, fileId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        byte[] archiveBytes = await DownloadWithFallbackAsync(hypertextTransferProtocolClient, archiveDownloadUrl, apiKey, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        await Console.Error.WriteLineAsync(value: "Reading modpack manifest").ConfigureAwait(continueOnCapturedContext: false);

        using ZipArchive archive = new(new MemoryStream(archiveBytes), ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(entryName: "manifest.json")
                            ?? throw new InvalidOperationException(message: "manifest.json not found");

        using var manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(manifestStream, CurseForgeManifestJsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                       ?? throw new InvalidOperationException(message: "failed to deserialize manifest.json");

        cancellationToken.ThrowIfCancellationRequested();

        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "mods"));
        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "resourcepacks"));
        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "shaderpacks"));

        var modsDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "mods"));
        var resourcePacksDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "resourcepacks"));
        var shaderPacksDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "shaderpacks"));

        await Console.Error.WriteLineAsync($"Prepared Minecraft directory: {minecraftDirectory}").ConfigureAwait(continueOnCapturedContext: false);

        string overridesFolder = manifest.Overrides ?? "overrides";

        if (archive.Entries.Any(entry => entry.FullName.StartsWith(overridesFolder + "/", StringComparison.Ordinal)))
        {
            await Console.Error.WriteLineAsync(value: "Installing modpack overrides").ConfigureAwait(continueOnCapturedContext: false);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool entryIsOverride = entry.FullName.StartsWith(overridesFolder + "/", StringComparison.Ordinal)
                                      && entry.FullName.Length > overridesFolder.Length + 1;

                if (!entryIsOverride)
                    continue;

                string targetPath = Path.GetFullPath(Path.Combine(minecraftDirectory, entry.FullName[(overridesFolder.Length + 1)..]));
                bool targetWithinMinecraftDirectory = targetPath.StartsWith(Path.GetFullPath(minecraftDirectory) + Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);

                if (!targetWithinMinecraftDirectory)
                    throw new InvalidDataException($"Modpack override escapes the Minecraft directory: {entry.FullName}");

                if (entry.FullName.EndsWith(value: '/'))
                {
                    var targetDirectoryInformation = Directory.CreateDirectory(targetPath);

                    continue;
                }

                var parentDirectoryInformation = Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? minecraftDirectory);

                using var sourceStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                using var targetStream = File.Create(targetPath);

                await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

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
            {
                await Console.Error.WriteLineAsync($"Resolving {requiredFileIds.Count} CurseForge files").ConfigureAwait(continueOnCapturedContext: false);

                List<CurseForgeFile> allFileMetadata = [];

                for (int batchStart = 0; batchStart < requiredFileIds.Count; batchStart += CurseForgeFilesBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    List<int> batch = [.. requiredFileIds.Skip(batchStart).Take(CurseForgeFilesBatchSize)];
                    var files = await curseForgeClient.GetFilesAsync(batch, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    allFileMetadata.AddRange(files);
                }

                int totalFileCount = allFileMetadata.Count;
                int downloadIndex = 0;

                foreach (var fileMeta in allFileMetadata)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    downloadIndex++;
                    ValidateFileName(fileMeta.FileName, fileMeta.ModId, fileMeta.Id);

                    string targetDirectory = DetermineTargetDirectory(fileMeta.FileName, minecraftDirectory);
                    var targetDirectoryInformation = Directory.CreateDirectory(targetDirectory);
                    string destinationPath = Path.Combine(targetDirectory, fileMeta.FileName);

                    if (File.Exists(destinationPath))
                    {
                        await Console.Error.WriteLineAsync($"[{downloadIndex}/{totalFileCount}] Already exists: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                        continue;
                    }

                    await Console.Error.WriteLineAsync($"[{downloadIndex}/{totalFileCount}] Downloading: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                    string fileDownloadUrl = await ResolveModFileDownloadUrlAsync(curseForgeClient, fileMeta.ModId, fileMeta.Id, fileMeta.DownloadUrl, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    byte[] fileBytes = await DownloadWithFallbackAsync(hypertextTransferProtocolClient, fileDownloadUrl, apiKey, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    await File.WriteAllBytesAsync(destinationPath, fileBytes, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                }
            }
            else
            {
                await Console.Error.WriteLineAsync(value: "No CurseForge files to download").ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        else
        {
            await Console.Error.WriteLineAsync(value: "No CurseForge files to download").ConfigureAwait(continueOnCapturedContext: false);
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
        string? sodiumUrl = null;

        File.Delete(sodiumPath);

        try
        {
            using HttpClient hypertextTransferProtocolClient = new();

            hypertextTransferProtocolClient.DefaultRequestHeaders.UserAgent.ParseAdd(input: "caunt/Void");

            Uri versionsUri = new($"https://api.modrinth.com/v2/project/AANobbMI/version?loaders={loaders}&game_versions={gameVersions}", UriKind.Absolute);

            using var versionsResponse = await hypertextTransferProtocolClient.GetAsync(versionsUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulVersionsResponse = versionsResponse.EnsureSuccessStatusCode();

            using var versionsStream = await successfulVersionsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            foreach (var version in versionsDocument.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty(propertyName: "files", out var files))
                    continue;

                foreach (var file in files.EnumerateArray())
                {
                    bool primaryFound = file.TryGetProperty(propertyName: "primary", out var primary);
                    bool uniformResourceLocatorFound = file.TryGetProperty(propertyName: "url", out var uniformResourceLocator);

                    if (primaryFound && primary.GetBoolean() && uniformResourceLocatorFound)
                    {
                        sodiumUrl = uniformResourceLocator.GetString();

                        break;
                    }
                }

                if (sodiumUrl is not null)
                    break;
            }

            if (sodiumUrl is null)
            {
                await Console.Error.WriteLineAsync($"Sodium was not found for {loader} Minecraft {minecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            Uri sodiumUri = new(sodiumUrl, UriKind.Absolute);

            using var sodiumResponse = await hypertextTransferProtocolClient.GetAsync(sodiumUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulSodiumResponse = sodiumResponse.EnsureSuccessStatusCode();

            using (var sodiumFile = File.Create(temporarySodiumPath))
                await successfulSodiumResponse.Content.CopyToAsync(sodiumFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            File.Move(temporarySodiumPath, sodiumPath, overwrite: true);
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

    private static async Task<string> PrepareCurseForgeAsync(
        string slug,
        int fileId,
        string curseForgeApiKey,
        Uri curseForgeApiBaseUri,
        string minecraftDirectory,
        CancellationToken cancellationToken
    )
    {
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));

        string markerFile = Path.Combine(minecraftDirectory, path2: ".curseforge-modpack");
        string versionFile = Path.Combine(minecraftDirectory, path2: ".curseforge-portablemc-version");
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
            portablemcVersion = await InstallModpack(slug, fileId, curseForgeApiKey, curseForgeApiBaseUri, minecraftDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await File.WriteAllTextAsync(versionFile, portablemcVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await File.WriteAllTextAsync(markerFile, marker, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await Console.Error.WriteLineAsync(value: "Installation marker updated").ConfigureAwait(continueOnCapturedContext: false);
        }

        return portablemcVersion;
    }

    private static async Task<string> ResolveDownloadUrlAsync(CurseForgeApiClient curseForgeClient, int projectId, int modFileId, CancellationToken cancellationToken)
    {
        var file = await curseForgeClient.GetModFileAsync(projectId, modFileId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(file.DownloadUrl))
            return file.DownloadUrl;

        string? downloadUrl = await curseForgeClient.GetModFileDownloadUrlAsync(projectId, modFileId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        return !string.IsNullOrEmpty(downloadUrl)
            ? downloadUrl
            : $"https://www.curseforge.com/api/v1/mods/{projectId}/files/{modFileId}/download";
    }

    private static async Task<string> ResolveModFileDownloadUrlAsync(
        CurseForgeApiClient curseForgeClient,
        int modId,
        int modFileId,
        string? softwareDevelopmentKitDownloadUrl,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(softwareDevelopmentKitDownloadUrl))
            return softwareDevelopmentKitDownloadUrl;

        string? downloadUrl = await curseForgeClient.GetModFileDownloadUrlAsync(modId, modFileId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        return !string.IsNullOrEmpty(downloadUrl)
            ? downloadUrl
            : $"https://www.curseforge.com/api/v1/mods/{modId}/files/{modFileId}/download";
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
            using TcpClient client = new();

            await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync($"{tracker.Token}\tcancel\t{requestId}".AsMemory(), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or InvalidOperationException or ObjectDisposedException)
        {
            await Console.Error.WriteLineAsync($"Minecraft agent command cancellation was unavailable: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
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

    private static bool UsesLegacyLwjgl(string version)
    {
        string[] versionComponents = version["mojang:".Length..].Split(separator: '.');

        return versionComponents.Length >= 2
            && int.TryParse(versionComponents[0], out int majorVersion)
            && int.TryParse(versionComponents[1], out int minorVersion)
            && majorVersion == 1
            && minorVersion <= 12;
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
}
