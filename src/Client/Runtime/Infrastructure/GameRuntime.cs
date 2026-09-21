using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    static async Task<string> InstallModpack(
        string slug,
        int fileIdentifier,
        string apiKey,
        Uri apiBaseUniformResourceIdentifier,
        string minecraftDirectory,
        CancellationToken cancellationToken
    )
    {
        using var hypertextTransferProtocolClient = new HttpClient();

        var curseForgeClient = new CurseForgeApiClient(hypertextTransferProtocolClient, apiBaseUniformResourceIdentifier, apiKey);

        await Console.Error.WriteLineAsync(value: "Resolving CurseForge project").ConfigureAwait(continueOnCapturedContext: false);
        var searchResult = await curseForgeClient.SearchModsAsync(MinecraftGameIdentifier, slug, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        var project = searchResult.FirstOrDefault(modpack => modpack.Slug == slug)
                      ?? throw new InvalidOperationException($"modpack not found: {slug}");

        await Console.Error.WriteLineAsync(value: "Downloading modpack archive").ConfigureAwait(continueOnCapturedContext: false);
        var archiveDownloadUniformResourceLocator = await ResolveDownloadUniformResourceLocatorAsync(curseForgeClient, project.Identifier, fileIdentifier, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        var archiveBytes = await DownloadWithFallbackAsync(hypertextTransferProtocolClient, archiveDownloadUniformResourceLocator, apiKey, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        await Console.Error.WriteLineAsync(value: "Reading modpack manifest").ConfigureAwait(continueOnCapturedContext: false);

        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);

        var manifestEntry = archive.GetEntry(entryName: "manifest.json")
                            ?? throw new InvalidOperationException(message: "manifest.json not found");

        using var manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(manifestStream, CurseForgeManifestJavaScriptObjectNotationOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                       ?? throw new InvalidOperationException(message: "failed to deserialize manifest.json");

        cancellationToken.ThrowIfCancellationRequested();

        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "mods"));
        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "resourcepacks"));
        DeleteDirectoryIfExists(Path.Combine(minecraftDirectory, path2: "shaderpacks"));

        var modsDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "mods"));
        var resourcePacksDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "resourcepacks"));
        var shaderPacksDirectoryInformation = Directory.CreateDirectory(Path.Combine(minecraftDirectory, path2: "shaderpacks"));

        await Console.Error.WriteLineAsync($"Prepared Minecraft directory: {minecraftDirectory}").ConfigureAwait(continueOnCapturedContext: false);

        var overridesFolder = manifest.Overrides ?? "overrides";

        if (archive.Entries.Any(entry => entry.FullName.StartsWith(overridesFolder + "/", StringComparison.Ordinal)))
        {
            await Console.Error.WriteLineAsync(value: "Installing modpack overrides").ConfigureAwait(continueOnCapturedContext: false);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryIsOverride = entry.FullName.StartsWith(overridesFolder + "/", StringComparison.Ordinal)
                                      && entry.FullName.Length > overridesFolder.Length + 1;

                if (!entryIsOverride)
                    continue;

                var targetPath = Path.GetFullPath(Path.Combine(minecraftDirectory, entry.FullName[(overridesFolder.Length + 1)..]));
                var targetWithinMinecraftDirectory = targetPath.StartsWith(Path.GetFullPath(minecraftDirectory) + Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);

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
            var requiredFileIdentifiers = new List<int>();

            foreach (var file in manifest.Files)
            {
                if (file.Required is false)
                    continue;

                var resolvedFileIdentifier = file.FileIdentifier;

                if (resolvedFileIdentifier is > 0)
                    requiredFileIdentifiers.Add(resolvedFileIdentifier.Value);
            }

            if (requiredFileIdentifiers.Count > 0)
            {
                await Console.Error.WriteLineAsync($"Resolving {requiredFileIdentifiers.Count} CurseForge files").ConfigureAwait(continueOnCapturedContext: false);

                var allFileMetadata = new List<CurseForgeFile>();

                for (var batchStart = 0; batchStart < requiredFileIdentifiers.Count; batchStart += CurseForgeFilesBatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = requiredFileIdentifiers.Skip(batchStart).Take(CurseForgeFilesBatchSize).ToList();
                    var files = await curseForgeClient.GetFilesAsync(batch, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    allFileMetadata.AddRange(files);
                }

                var totalFileCount = allFileMetadata.Count;
                var downloadIndex = 0;

                foreach (var fileMeta in allFileMetadata)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    downloadIndex++;
                    ValidateFileName(fileMeta.FileName, fileMeta.ModIdentifier, fileMeta.Identifier);

                    var targetDirectory = DetermineTargetDirectory(fileMeta.FileName, minecraftDirectory);
                    var targetDirectoryInformation = Directory.CreateDirectory(targetDirectory);
                    var destinationPath = Path.Combine(targetDirectory, fileMeta.FileName);

                    if (File.Exists(destinationPath))
                    {
                        await Console.Error.WriteLineAsync($"[{downloadIndex}/{totalFileCount}] Already exists: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                        continue;
                    }

                    await Console.Error.WriteLineAsync($"[{downloadIndex}/{totalFileCount}] Downloading: {fileMeta.FileName}").ConfigureAwait(continueOnCapturedContext: false);

                    var fileDownloadUniformResourceLocator = await ResolveModFileDownloadUniformResourceLocatorAsync(curseForgeClient, fileMeta.ModIdentifier, fileMeta.Identifier, fileMeta.DownloadUniformResourceLocator, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                    var fileBytes = await DownloadWithFallbackAsync(hypertextTransferProtocolClient, fileDownloadUniformResourceLocator, apiKey, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

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

        var minecraftVersion = manifest.Minecraft?.Version
                               ?? throw new InvalidOperationException(message: "minecraft.version missing");

        var portablemcVersion = $"mojang:{minecraftVersion}";

        if (manifest.Minecraft.ModLoaders is not null)
        {
            foreach (var loader in manifest.Minecraft.ModLoaders)
            {
                if (loader.Primary != true)
                    continue;

                var loaderIdentifier = loader.Identifier ?? "";

                if (loaderIdentifier.StartsWith(value: "neoforge-", StringComparison.Ordinal))
                {
                    portablemcVersion = $"neoforge::{loaderIdentifier["neoforge-".Length..]}";
                }
                else if (loaderIdentifier.StartsWith(value: "forge-", StringComparison.Ordinal))
                {
                    var forgeVersion = loaderIdentifier["forge-".Length..];
                    portablemcVersion = forgeVersion.StartsWith($"{minecraftVersion}-", StringComparison.Ordinal)
                        ? $"forge::{forgeVersion}"
                        : $"forge::{minecraftVersion}-{forgeVersion}";
                }
                else
                {
                    portablemcVersion = loaderIdentifier.StartsWith(value: "fabric-", StringComparison.Ordinal)
                        ? $"fabric:{minecraftVersion}:{loaderIdentifier["fabric-".Length..]}"
                        : loaderIdentifier.StartsWith(value: "quilt-", StringComparison.Ordinal)
                        ? $"quilt:{minecraftVersion}:{loaderIdentifier["quilt-".Length..]}"
                        : throw new InvalidOperationException($"Unsupported mod loader for CurseForge modpack '{slug}' (file id: {fileIdentifier}): {loaderIdentifier}");
                }

                break;
            }
        }

        return portablemcVersion;
    }

    static async Task InstallSodiumAsync(string modsDirectory, string portableMinecraftVersion, string minecraftVersion, CancellationToken cancellationToken)
    {
        var sodiumPath = Path.Combine(modsDirectory, path2: "sodium.jar");
        var temporarySodiumPath = Path.Combine(modsDirectory, $".sodium.jar.{Guid.NewGuid():N}");
        var separatorIndex = portableMinecraftVersion.IndexOf(value: ':', StringComparison.Ordinal);
        var loader = separatorIndex < 0 ? "mojang" : portableMinecraftVersion[..separatorIndex];
        var loaders = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }));
        var gameVersions = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }));
        string? sodiumUniformResourceLocator = null;

        File.Delete(sodiumPath);

        try
        {
            using var hypertextTransferProtocolClient = new HttpClient();

            hypertextTransferProtocolClient.DefaultRequestHeaders.UserAgent.ParseAdd(input: "caunt/Void");

            var versionsUniformResourceIdentifier = new Uri($"https://api.modrinth.com/v2/project/AANobbMI/version?loaders={loaders}&game_versions={gameVersions}", UriKind.Absolute);

            using var versionsResponse = await hypertextTransferProtocolClient.GetAsync(versionsUniformResourceIdentifier, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulVersionsResponse = versionsResponse.EnsureSuccessStatusCode();

            using var versionsStream = await successfulVersionsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using var versionsDocument = await JsonDocument.ParseAsync(versionsStream, cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            foreach (var version in versionsDocument.RootElement.EnumerateArray())
            {
                if (!version.TryGetProperty(propertyName: "files", out var files))
                    continue;

                foreach (var file in files.EnumerateArray())
                {
                    var primaryFound = file.TryGetProperty(propertyName: "primary", out var primary);
                    var uniformResourceLocatorFound = file.TryGetProperty(propertyName: "url", out var uniformResourceLocator);

                    if (primaryFound && primary.GetBoolean() && uniformResourceLocatorFound)
                    {
                        sodiumUniformResourceLocator = uniformResourceLocator.GetString();

                        break;
                    }
                }

                if (sodiumUniformResourceLocator is not null)
                    break;
            }

            if (sodiumUniformResourceLocator is null)
            {
                await Console.Error.WriteLineAsync($"Sodium was not found for {loader} Minecraft {minecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            var sodiumUniformResourceIdentifier = new Uri(sodiumUniformResourceLocator, UriKind.Absolute);

            using var sodiumResponse = await hypertextTransferProtocolClient.GetAsync(sodiumUniformResourceIdentifier, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

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

    private static bool IsProcessGroupRunning(int processGroupIdentifier)
    {
        foreach (var processDirectory in Directory.EnumerateDirectories(path: "/proc"))
        {
            try
            {
                var status = File.ReadAllText(Path.Combine(processDirectory, path2: "stat"));
                var commandEnd = status.LastIndexOf(value: ')');

                if (commandEnd < 0)
                    continue;

                var fields = status[(commandEnd + 1)..].Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);
                var candidateProcessGroupIdentifier = 0;

                var candidateParsed = fields.Length > 2
                                      && fields[0] is not "Z"
                                      && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out candidateProcessGroupIdentifier);

                if (candidateParsed && candidateProcessGroupIdentifier == processGroupIdentifier)
                    return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Process state changed during process-group inspection: {exception.Message}");
            }
        }

        return false;
    }

    static void KillProcess(Process process)
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

    static async Task<string> PrepareCurseForgeAsync(
        string slug,
        int fileIdentifier,
        string curseForgeApiKey,
        Uri curseForgeApiBaseUniformResourceIdentifier,
        string minecraftDirectory,
        CancellationToken cancellationToken
    )
    {
        GC.KeepAlive(Directory.CreateDirectory(minecraftDirectory));

        var markerFile = Path.Combine(minecraftDirectory, path2: ".curseforge-modpack");
        var versionFile = Path.Combine(minecraftDirectory, path2: ".curseforge-portablemc-version");
        var marker = $"{slug} {fileIdentifier}";
        var existingMarker = File.Exists(markerFile) ? (await File.ReadAllTextAsync(markerFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Trim() : "";
        string portablemcVersion;

        await Console.Error.WriteLineAsync($"Starting CurseForge modpack '{slug}' file '{fileIdentifier}'").ConfigureAwait(continueOnCapturedContext: false);

        if (existingMarker == marker)
        {
            if (!File.Exists(versionFile))
                throw new InvalidOperationException($"PortableMC version cache file is missing: {versionFile}");

            portablemcVersion = (await File.ReadAllTextAsync(versionFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)).Trim();

            await Console.Error.WriteLineAsync($"Using existing installation in {minecraftDirectory}").ConfigureAwait(continueOnCapturedContext: false);
        }
        else
        {
            portablemcVersion = await InstallModpack(slug, fileIdentifier, curseForgeApiKey, curseForgeApiBaseUniformResourceIdentifier, minecraftDirectory, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await File.WriteAllTextAsync(versionFile, portablemcVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            await File.WriteAllTextAsync(markerFile, marker, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            await Console.Error.WriteLineAsync(value: "Installation marker updated").ConfigureAwait(continueOnCapturedContext: false);
        }

        return portablemcVersion;
    }

    static async Task<string> ResolveDownloadUniformResourceLocatorAsync(CurseForgeApiClient curseForgeClient, int projectIdentifier, int modFileIdentifier, CancellationToken cancellationToken)
    {
        var file = await curseForgeClient.GetModFileAsync(projectIdentifier, modFileIdentifier, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(file.DownloadUniformResourceLocator))
            return file.DownloadUniformResourceLocator;

        var downloadUniformResourceLocator = await curseForgeClient.GetModFileDownloadUniformResourceLocatorAsync(projectIdentifier, modFileIdentifier, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        return !string.IsNullOrEmpty(downloadUniformResourceLocator)
            ? downloadUniformResourceLocator
            : $"https://www.curseforge.com/api/v1/mods/{projectIdentifier}/files/{modFileIdentifier}/download";
    }

    static async Task<string> ResolveModFileDownloadUniformResourceLocatorAsync(
        CurseForgeApiClient curseForgeClient,
        int modIdentifier,
        int modFileIdentifier,
        string? softwareDevelopmentKitDownloadUniformResourceLocator,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(softwareDevelopmentKitDownloadUniformResourceLocator))
            return softwareDevelopmentKitDownloadUniformResourceLocator;

        var downloadUniformResourceLocator = await curseForgeClient.GetModFileDownloadUniformResourceLocatorAsync(modIdentifier, modFileIdentifier, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        cancellationToken.ThrowIfCancellationRequested();

        return !string.IsNullOrEmpty(downloadUniformResourceLocator)
            ? downloadUniformResourceLocator
            : $"https://www.curseforge.com/api/v1/mods/{modIdentifier}/files/{modFileIdentifier}/download";
    }

    static async Task<ProcessBytesResult> RunProcessBytesAsync(ProcessStartInfo processStartInformation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var process = Process.Start(processStartInformation)
                      ?? throw new InvalidOperationException($"failed to start {processStartInformation.FileName}");

        var standardOutput = new MemoryStream();

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

    static async Task TryCancelAgentCommandAsync(GameTrackerConnection tracker, int port, string requestIdentifier)
    {
        try
        {
            using var client = new TcpClient();

            await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);

            using var stream = client.GetStream();

            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync($"{tracker.Token}\tcancel\t{requestIdentifier}".AsMemory(), CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or InvalidOperationException or ObjectDisposedException)
        {
            await Console.Error.WriteLineAsync($"Minecraft agent command cancellation was unavailable: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private static bool TryReadParentProcessIdentifier(string processDirectory, out int parentProcessIdentifier)
    {
        parentProcessIdentifier = 0;

        try
        {
            foreach (var line in File.ReadLines(Path.Combine(processDirectory, path2: "status")))
            {
                if (!line.StartsWith(value: "PPid:", StringComparison.Ordinal))
                    continue;

                return int.TryParse(line.AsSpan("PPid:".Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out parentProcessIdentifier);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Process ended before its parent identifier could be read: {exception.Message}");
        }

        return false;
    }

    static bool UsesLegacyLwjgl(string version)
    {
        var versionComponents = version["mojang:".Length..].Split(separator: '.');

        return versionComponents.Length >= 2
            && int.TryParse(versionComponents[0], out var majorVersion)
            && int.TryParse(versionComponents[1], out var minorVersion)
            && majorVersion == 1
            && minorVersion <= 12;
    }

    static void ValidateFileName(string fileName, int modIdentifier, int modFileIdentifier)
    {
        var safeName = Path.GetFileName(fileName);

        var fileNameIsSafe = safeName == fileName
                             && !string.IsNullOrEmpty(safeName)
                             && safeName is not "." and not ".."
                             && !safeName.Contains(value: '/', StringComparison.Ordinal)
                             && !safeName.Contains(value: '\\', StringComparison.Ordinal)
                             && !safeName.Any(char.IsControl);

        if (!fileNameIsSafe)
            throw new InvalidOperationException($"Unexpected CurseForge file name '{fileName}' for mod id '{modIdentifier}' and file id '{modFileIdentifier}'");
    }

    private static void ValidatePlayer(GamePlayer player)
    {
        var positionIsFinite = double.IsFinite(player.Position.XCoordinate)
                               && double.IsFinite(player.Position.YCoordinate)
                               && double.IsFinite(player.Position.ZCoordinate);

        if (!positionIsFinite)
            throw PlayersUnavailable(message: "The Minecraft player tracker returned a non-finite player coordinate");

        ValidateRotation(player.Body, player.Head);
    }

    private static void ValidatePlayer(RemoteGamePlayer player)
    {
        var positionIsFinite = double.IsFinite(player.Position.XCoordinate)
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

    static async Task WaitForKilledProcessAsync(Process process)
    {
        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        await waitTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    static async Task WaitForProcessExitAsync(Process process, string processName, TimeSpan timeout, CancellationToken cancellationToken, bool killOnTimeout = true)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

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

    private static async Task WaitForProcessGroupExitAsync(int processGroupIdentifier, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (IsProcessGroupRunning(processGroupIdentifier))
        {
            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException($"Minecraft process group did not exit within {timeout.TotalSeconds:F1} seconds");

            await Task.Delay(millisecondsDelay: 50, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }
}
