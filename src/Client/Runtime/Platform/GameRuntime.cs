using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

using File = System.IO.File;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private static readonly ConcurrentDictionary<string, bool> ArmNativesAvailability = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the LWJGL libraries and Java version from a vanilla version JSON so ARM64 natives follow the launched Minecraft version.
    /// </summary>
    internal static ArmLwjglPlan CreateArmLwjglPlan(JsonElement versionMetadata)
    {
        int javaMajorVersion = ReadJavaMajorVersion(versionMetadata);
        List<LwjglLibrary> libraries = [];

        if (versionMetadata.TryGetProperty(propertyName: "libraries", out var libraryElements))
        {
            foreach (var libraryElement in libraryElements.EnumerateArray())
            {
                if (ReadLwjglLibrary(libraryElement) is { } library)
                    libraries.Add(library);
            }
        }

        if (libraries.Count == 0)
            return new ArmLwjglPlan(UsesLegacyLwjgl: true, FixVersion: null, [], UnsafeCoreDownloadUri: null, javaMajorVersion);

        string highestVersion = libraries[index: 0].Version;
        ushort[] highestComponents = ParseVersionComponents(highestVersion, maximumCount: 3, minimumCount: 1, ignoreSuffix: true) ?? [0, 0, 0];

        foreach (var library in libraries)
        {
            ushort[] components = ParseVersionComponents(library.Version, maximumCount: 3, minimumCount: 1, ignoreSuffix: true) ?? [0, 0, 0];

            if (CompareVersionComponents(components, highestComponents) <= 0)
                continue;

            highestVersion = library.Version;
            highestComponents = components;
        }

        // LWJGL 3.4 builds are tied to the Minecraft release that ships them, while older releases share the 3.3.3 ARM64 natives.
        bool usesVersionLwjgl = CompareVersionComponents(highestComponents, [3, 4, 0]) >= 0;
        string fixVersion = usesVersionLwjgl ? highestVersion : PortableMinecraftArmLwjglMinimumVersion;
        string[] modules = [.. libraries.Where(static library => library.Classifier is null).Select(static library => library.Module).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Uri? unsafeCoreDownloadUri = null;

        if (usesVersionLwjgl && !modules.Contains(value: "lwjgl", StringComparer.Ordinal))
        {
            var unsafeCore = libraries.FirstOrDefault(library => library is { Module: "lwjgl", Classifier: "unsafe", DownloadUrl: not null } && library.Version == highestVersion);

            if (unsafeCore?.DownloadUrl is { } downloadUrl)
                unsafeCoreDownloadUri = new Uri(downloadUrl, UriKind.Absolute);
        }

        return new ArmLwjglPlan(UsesLegacyLwjgl: false, fixVersion, modules, unsafeCoreDownloadUri, javaMajorVersion);
    }

    internal static IReadOnlyList<string> CreateArmPortableMinecraftArguments(ArmLaunchOptions? options, IReadOnlyCollection<string> requestedArguments, bool isLaunch)
    {
        if (options is null)
            return [];

        List<string> arguments = [];

        if (options.UsesLegacyJvm)
        {
            if (isLaunch && !HasPortableMinecraftArgument(requestedArguments, argumentName: "--jvm"))
                arguments.AddRange(["--jvm", PortableMinecraftLegacyJvmPath]);

            return arguments;
        }

        if (options.FixLwjglVersion is not null && !HasPortableMinecraftArgument(requestedArguments, argumentName: "--fix-lwjgl"))
        {
            arguments.AddRange(["--fix-lwjgl", options.FixLwjglVersion]);

            foreach (string classPath in options.IncludedClassPaths)
                arguments.AddRange(["--include-class", classPath]);
        }

        if (!HasPortableMinecraftArgument(requestedArguments, argumentName: "--exclude-lib"))
        {
            foreach (string library in options.ExcludedLibraries)
                arguments.AddRange(["--exclude-lib", library]);
        }

        return arguments;
    }

    internal static string? GetMinecraftVersionFromNeoForgeVersion(string neoForgeVersion)
    {
        if (neoForgeVersion == "47.1.82" || neoForgeVersion.StartsWith(value: "1.20.1-", StringComparison.Ordinal))
            return "1.20.1";

        ushort[]? components = ParseVersionComponents(neoForgeVersion, maximumCount: 3, minimumCount: 2, ignoreSuffix: true);

        return components is not [var majorVersion, var minorVersion, var patchVersion]
            ? null
            : majorVersion >= 26
            ? patchVersion == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{majorVersion}.{minorVersion}")
                : string.Create(CultureInfo.InvariantCulture, $"{majorVersion}.{minorVersion}.{patchVersion}")
            : minorVersion == 0
            ? string.Create(CultureInfo.InvariantCulture, $"1.{majorVersion}")
            : string.Create(CultureInfo.InvariantCulture, $"1.{majorVersion}.{minorVersion}");
    }

    /// <summary>
    /// Returns the vanilla Minecraft version (or the release/snapshot alias) that a PortableMC version string launches.
    /// </summary>
    internal static string? GetMinecraftVersionFromPortableVersion(string portableMinecraftVersion)
    {
        string[] parts = portableMinecraftVersion.Split(separator: ':');
        string gameVersion = parts.Length > 1 ? parts[1] : "";
        string loaderVersion = parts.Length > 2 ? parts[2] : "";

        return gameVersion.Length > 0
            ? gameVersion
            : parts[0] switch
            {
                "mojang" or "fabric" or "quilt" or "legacyfabric" or "babric" => "release",
                "neoforge" => loaderVersion.Length > 0 ? GetMinecraftVersionFromNeoForgeVersion(loaderVersion) : "release",
                "forge" => loaderVersion.Length > 0 ? GetMinecraftVersionFromForgeVersion(loaderVersion) : "release",
                _ => null
            };
    }

    internal static bool IsCompatibleJavaMajorVersion(int requiredMajorVersion, int installedMajorVersion)
    {
        return requiredMajorVersion <= 8 ? installedMajorVersion == requiredMajorVersion : installedMajorVersion >= requiredMajorVersion;
    }

    internal static string? SelectAzulJreDownloadUrl(JsonElement packages)
    {
        foreach (var package in packages.EnumerateArray())
        {
            string name = package.TryGetProperty(propertyName: "name", out var nameElement) ? nameElement.GetString() ?? "" : "";
            bool packageMatches = name.Contains(value: "-linux_aarch64.tar.gz", StringComparison.Ordinal) && !name.Contains(value: "-fx-", StringComparison.Ordinal);

            if (packageMatches && package.TryGetProperty(propertyName: "download_url", out var downloadUrl))
                return downloadUrl.GetString();
        }

        return null;
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, Uri downloadUri, string destinationPath, CancellationToken cancellationToken)
    {
        var destinationDirectoryInformation = Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
        string temporaryPath = Path.Combine(destinationDirectoryInformation.FullName, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}");

        try
        {
            using var response = await httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulResponse = response.EnsureSuccessStatusCode();

            using (var temporaryFile = File.Create(temporaryPath))
                await successfulResponse.Content.CopyToAsync(temporaryFile, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task<string> EnsureLwjglCoreFileAsync(
        HttpClient httpClient,
        string minecraftDirectory,
        string lwjglVersion,
        string fileName,
        Uri downloadUri,
        CancellationToken cancellationToken
    )
    {
        string bakedPath = Path.Combine(PortableMinecraftBakedFilesDirectory, fileName);

        if (File.Exists(bakedPath))
            return bakedPath;

        string libraryPath = Path.Combine([minecraftDirectory, "libraries", "org", "lwjgl", "lwjgl", lwjglVersion, fileName]);

        if (!File.Exists(libraryPath))
            await DownloadFileAsync(httpClient, downloadUri, libraryPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return libraryPath;
    }

    private static string? GetMinecraftVersionFromForgeVersion(string forgeVersion)
    {
        int separatorIndex = forgeVersion.IndexOf(value: '-', StringComparison.Ordinal);

        if (separatorIndex <= 0)
            return null;

        string gameVersion = forgeVersion[..separatorIndex];

        return gameVersion == "1.7.10_pre4" ? "1.7.10-pre4" : gameVersion;
    }

    private static async Task<bool> HasArmNativesAsync(HttpClient httpClient, string minecraftDirectory, string module, string lwjglVersion, CancellationToken cancellationToken)
    {
        string fileName = $"{module}-{lwjglVersion}-{PortableMinecraftArmNativesClassifier}.jar";

        if (File.Exists(Path.Combine([minecraftDirectory, "libraries", "org", "lwjgl", module, lwjglVersion, fileName])))
            return true;

        string cacheKey = $"{module}:{lwjglVersion}";

        if (ArmNativesAvailability.TryGetValue(cacheKey, out bool cachedAvailability))
            return cachedAvailability;

        Uri nativesUri = new($"{MavenCentralRepositoryUrl}/org/lwjgl/{module}/{lwjglVersion}/{fileName}", UriKind.Absolute);

        for (int attempt = 1; attempt <= UpstreamRequestAttempts; attempt++)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Head, nativesUri);

                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                if (response.StatusCode is HttpStatusCode.NotFound || response.IsSuccessStatusCode)
                {
                    bool available = response.IsSuccessStatusCode;
                    ArmNativesAvailability[cacheKey] = available;

                    return available;
                }

                await Console.Error.WriteLineAsync($"Checking {nativesUri} returned {(int)response.StatusCode} on attempt {attempt}/{UpstreamRequestAttempts}").ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (Exception exception) when (IsTransientUpstreamFailure(exception, cancellationToken))
            {
                await Console.Error.WriteLineAsync($"Checking {nativesUri} failed on attempt {attempt}/{UpstreamRequestAttempts}: {exception.Message}").ConfigureAwait(continueOnCapturedContext: false);
            }

            if (attempt < UpstreamRequestAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        await Console.Error.WriteLineAsync($"ARM64 natives for {module} {lwjglVersion} could not be verified; excluding them").ConfigureAwait(continueOnCapturedContext: false);

        return false;
    }

    private static int ReadJavaMajorVersion(JsonElement versionMetadata)
    {
        return !versionMetadata.TryGetProperty(propertyName: "javaVersion", out var javaVersion)
            ? 8
            : !javaVersion.TryGetProperty(propertyName: "majorVersion", out var majorVersion)
            ? 8
            : majorVersion.TryGetInt32(out int value) ? value : 8;
    }

    private static LwjglLibrary? ReadLwjglLibrary(JsonElement libraryElement)
    {
        string? coordinates = libraryElement.TryGetProperty(propertyName: "name", out var name) ? name.GetString() : null;
        string[] parts = coordinates?.Split(separator: ':') ?? [];

        if (parts.Length < 3 || parts[0] != "org.lwjgl")
            return null;

        string? downloadUrl = null;
        bool downloadsFound = libraryElement.TryGetProperty(propertyName: "downloads", out var downloads);

        if (downloadsFound && downloads.TryGetProperty(propertyName: "artifact", out var artifact))
            downloadUrl = artifact.TryGetProperty(propertyName: "url", out var url) ? url.GetString() : null;

        return new LwjglLibrary(parts[1], parts[2], parts.Length > 3 ? parts[3] : null, downloadUrl);
    }

    private static async Task<JsonDocument> ReadVersionMetadataAsync(HttpClient httpClient, string minecraftDirectory, string minecraftVersion, CancellationToken cancellationToken)
    {
        Uri manifestUri = new(MojangVersionManifestUrl, UriKind.Absolute);
        MojangVersionManifest? manifest = null;
        string versionId = minecraftVersion;

        if (versionId is "release" or "snapshot")
        {
            manifest = ParseMojangVersionManifest(await GetStringWithRetriesAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));
            versionId = versionId is "release" ? manifest.LatestRelease : manifest.LatestSnapshot;
        }

        string localPath = Path.Combine(minecraftDirectory, path2: "versions", versionId, $"{versionId}.json");

        if (File.Exists(localPath))
            return JsonDocument.Parse(await File.ReadAllTextAsync(localPath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));

        manifest ??= ParseMojangVersionManifest(await GetStringWithRetriesAsync(httpClient, manifestUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false));

        var version = manifest.Versions.FirstOrDefault(candidate => candidate.Id == versionId)
                      ?? throw new InvalidOperationException($"Minecraft {versionId} is not listed in the Mojang version manifest");

        return JsonDocument.Parse(
            await GetStringWithRetriesAsync(httpClient, version.MetadataUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
        );
    }

    private async Task EnsureArmJavaRuntimeAsync(HttpClient httpClient, int javaMajorVersion, string minecraftVersion, CancellationToken cancellationToken)
    {
        IEnumerable<string> javaDirectories = Directory.Exists(PortableMinecraftJvmDirectory) ? Directory.EnumerateDirectories(PortableMinecraftJvmDirectory, searchPattern: "zulu-*") : [];

        foreach (string javaDirectory in javaDirectories)
        {
            bool installedVersionParsed = int.TryParse(
                Path.GetFileName(javaDirectory)["zulu-".Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int installedMajorVersion
            );

            bool installedJavaUsable = installedVersionParsed && File.Exists(Path.Combine(javaDirectory, path2: "bin", path3: "java"));

            if (installedJavaUsable && IsCompatibleJavaMajorVersion(javaMajorVersion, installedMajorVersion))
                return;
        }

        string packagesQuery = string.Create(
            CultureInfo.InvariantCulture,
            $"?java_version={javaMajorVersion}&os=linux&arch=arm&archive_type=tar.gz&java_package_type=jre&release_status=ga&availability_types=CA"
        );

        using JsonDocument packages = JsonDocument.Parse(
            await GetStringWithRetriesAsync(httpClient, new Uri(AzulPackagesUrl + packagesQuery, UriKind.Absolute), cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
        );

        string downloadUrl = SelectAzulJreDownloadUrl(packages.RootElement)
                             ?? throw new InvalidOperationException($"No ARM64 Azul Zulu JRE {javaMajorVersion} is available for Minecraft {minecraftVersion}");

        string archivePath = Path.Combine(Path.GetTempPath(), $"zulu-{javaMajorVersion}-{Guid.NewGuid():N}.tar.gz");
        string stagingDirectory = Path.Combine(PortableMinecraftJvmDirectory, $".zulu-{javaMajorVersion}.{Guid.NewGuid():N}");
        string targetDirectory = Path.Combine(PortableMinecraftJvmDirectory, $"zulu-{javaMajorVersion}");

        try
        {
            await DownloadFileAsync(httpClient, new Uri(downloadUrl, UriKind.Absolute), archivePath, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var stagingDirectoryInformation = Directory.CreateDirectory(stagingDirectory);

            var extractionResult = await RunProcessTextAsync(
                CreateProcessStartInformation(fileName: "tar", ["--extract", "--gzip", "--file", archivePath, "--directory", stagingDirectory, "--strip-components=1"]),
                TimeSpan.FromMinutes(minutes: 5),
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);

            if (extractionResult.ExitCode is not 0)
                throw new InvalidOperationException($"Extracting Azul Zulu JRE {javaMajorVersion} failed: {extractionResult.StandardError}");

            DeleteDirectoryIfExists(targetDirectory);
            Directory.Move(stagingDirectory, targetDirectory);

            if (!File.Exists(Path.Combine(targetDirectory, path2: "bin", path3: "java")))
                throw new InvalidOperationException($"Azul Zulu JRE {javaMajorVersion} has no bin/java");

            await Console.Error.WriteLineAsync($"Installed Azul Zulu JRE {javaMajorVersion} for Minecraft {minecraftVersion}").ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            File.Delete(archivePath);
            DeleteDirectoryIfExists(stagingDirectory);
        }
    }

    private async Task<ArmLaunchOptions?> PrepareArmLaunchOptionsAsync(string minecraftDirectory, string? minecraftVersion, CancellationToken cancellationToken)
    {
        if (!File.Exists(PortableMinecraftLegacyJvmExecutablePath))
            return null;

        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            await Console.Error.WriteLineAsync(value: "The Minecraft version could not be determined; ARM64 LWJGL options are skipped").ConfigureAwait(continueOnCapturedContext: false);

            return null;
        }

        using HttpClient httpClient = CreateUpstreamHttpClient();

        using var versionMetadata = await ReadVersionMetadataAsync(httpClient, minecraftDirectory, minecraftVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        var plan = CreateArmLwjglPlan(versionMetadata.RootElement);

        await EnsureArmJavaRuntimeAsync(httpClient, plan.JavaMajorVersion, minecraftVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (plan.UsesLegacyLwjgl)
        {
            await Console.Error.WriteLineAsync($"ARM64 Minecraft {minecraftVersion}: LWJGL 2 through {PortableMinecraftLegacyJvmPath}, Java {plan.JavaMajorVersion}").ConfigureAwait(continueOnCapturedContext: false);

            return new ArmLaunchOptions(UsesLegacyJvm: true, FixLwjglVersion: null, [], []);
        }

        string fixVersion = plan.FixVersion ?? PortableMinecraftArmLwjglMinimumVersion;
        List<string> excludedLibraries = [];
        List<string> includedClassPaths = [];

        foreach (string module in plan.Modules)
        {
            bool nativesAvailable = await HasArmNativesAsync(httpClient, minecraftDirectory, module, fixVersion, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            if (!nativesAvailable)
                excludedLibraries.Add($"org.lwjgl:{module}:*:{PortableMinecraftArmNativesClassifier}");
        }

        // --fix-lwjgl drops classified jars, so a core that only ships as ":unsafe" is added back with its ARM64 natives.
        if (plan.UnsafeCoreDownloadUri is { } unsafeCoreDownloadUri)
        {
            string nativesFileName = $"lwjgl-{fixVersion}-{PortableMinecraftArmNativesClassifier}.jar";
            Uri nativesUri = new($"{MavenCentralRepositoryUrl}/org/lwjgl/lwjgl/{fixVersion}/{nativesFileName}", UriKind.Absolute);

            includedClassPaths.Add(
                await EnsureLwjglCoreFileAsync(httpClient, minecraftDirectory, fixVersion, $"lwjgl-{fixVersion}-unsafe.jar", unsafeCoreDownloadUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
            );
            includedClassPaths.Add(
                await EnsureLwjglCoreFileAsync(httpClient, minecraftDirectory, fixVersion, nativesFileName, nativesUri, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
            );
        }

        string excludedDescription = excludedLibraries.Count > 0 ? string.Join(separator: ", ", excludedLibraries) : "none";

        await Console.Error.WriteLineAsync(
            $"ARM64 Minecraft {minecraftVersion}: LWJGL {fixVersion}, Java {plan.JavaMajorVersion}, excluded natives: {excludedDescription}, included classes: {includedClassPaths.Count}"
        ).ConfigureAwait(continueOnCapturedContext: false);

        return new ArmLaunchOptions(UsesLegacyJvm: false, fixVersion, excludedLibraries, includedClassPaths);
    }

    internal sealed record ArmLaunchOptions(bool UsesLegacyJvm, string? FixLwjglVersion, IReadOnlyList<string> ExcludedLibraries, IReadOnlyList<string> IncludedClassPaths);

    internal sealed record ArmLwjglPlan(bool UsesLegacyLwjgl, string? FixVersion, IReadOnlyList<string> Modules, Uri? UnsafeCoreDownloadUri, int JavaMajorVersion);

    private sealed record LwjglLibrary(string Module, string Version, string? Classifier, string? DownloadUrl);
}
