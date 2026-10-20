using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    /// <summary>
    /// Moves a modpack that an earlier client installed in the game directory itself into its own directory, so starting it again needs
    /// no download, and the other starts no longer load its files.
    /// </summary>
    internal static void MoveModpackIntoItsDirectory(string minecraftDirectory, string modpackDirectory)
    {
        bool installedInGameDirectory = File.Exists(Path.Combine(minecraftDirectory, CurseForgeMarkerFileName));

        if (!installedInGameDirectory || File.Exists(Path.Combine(modpackDirectory, CurseForgeMarkerFileName)))
            return;

        GC.KeepAlive(Directory.CreateDirectory(modpackDirectory));
        string overridesListPath = Path.Combine(minecraftDirectory, CurseForgeOverridesFileName);
        string directory = $"{Path.GetFullPath(minecraftDirectory)}{Path.DirectorySeparatorChar}";

        foreach (string folder in new[] { "mods", "resourcepacks", "shaderpacks" })
        {
            string source = Path.Combine(minecraftDirectory, folder);
            string target = Path.Combine(modpackDirectory, folder);

            if (Directory.Exists(source) && !Directory.Exists(target))
                Directory.Move(source, target);
        }

        // Override files at the top of the game directory stay, because options.txt there belongs to the consumer.
        foreach (string relativePath in File.Exists(overridesListPath) ? File.ReadAllLines(overridesListPath) : [])
        {
            string source = Path.GetFullPath(Path.Combine(directory, relativePath));
            string target = Path.Combine(modpackDirectory, relativePath);
            bool inSubdirectory = relativePath.Contains(value: '/', StringComparison.Ordinal) && source.StartsWith(directory, StringComparison.Ordinal);

            if (!inSubdirectory || !File.Exists(source) || File.Exists(target))
                continue;

            GC.KeepAlive(Directory.CreateDirectory(Path.GetDirectoryName(target) ?? modpackDirectory));
            File.Move(source, target);
        }

        // The marker moves last, so a move that stops part-way continues on the next start.
        foreach (string name in new[] { CurseForgeOverridesFileName, CurseForgeVersionFileName, CurseForgeMarkerFileName })
        {
            if (File.Exists(Path.Combine(minecraftDirectory, name)))
                File.Move(Path.Combine(minecraftDirectory, name), Path.Combine(modpackDirectory, name), overwrite: true);
        }
    }

    private readonly record struct ModpackOverride(ZipArchiveEntry Entry, string RelativePath, string TargetPath);

    internal sealed record CurseForgeFileHash([property: JsonPropertyName("value")] string? Value, [property: JsonPropertyName("algo")] int Algo);

    private sealed class CurseForgeApiClient(HttpClient httpClient, Uri baseUri, string apiKey, TimeSpan retryDelay)
    {
        private readonly string _apiKey = apiKey;
        private readonly Uri _baseUri = baseUri;
        private readonly HttpClient _httpClient = httpClient;
        private readonly TimeSpan _retryDelay = retryDelay;
        private readonly JsonSerializerOptions _javaScriptObjectNotationOptions = new(JsonSerializerDefaults.Web);

        public async Task<List<CurseForgeFile>> GetFilesAsync(List<int> fileIds, CancellationToken cancellationToken)
        {
            var response = await PostAsync<CurseForgeApiListResponse<CurseForgeFile>>(relativeUrl: "v1/mods/files", new CurseForgeFilesRequest(fileIds), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? [];
        }

        public async Task<CurseForgeFile> GetModFileAsync(int modId, int fileId, CancellationToken cancellationToken)
        {
            var response = await GetAsync<CurseForgeApiResponse<CurseForgeFile>>($"v1/mods/{modId}/files/{fileId}", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? throw new InvalidOperationException($"CurseForge file not found: mod {modId}, file {fileId}");
        }

        public async Task<List<CurseForgeProject>> SearchModsAsync(int gameId, string slug, CancellationToken cancellationToken)
        {
            string slugQuery = Uri.EscapeDataString(slug);
            var response = await GetAsync<CurseForgeApiListResponse<CurseForgeProject>>($"v1/mods/search?gameId={gameId}&slug={slugQuery}", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? [];
        }

        private async Task<TResponse> GetAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
        {
            return await SendAsync<TResponse>(() => new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativeUrl)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        private async Task<TResponse> PostAsync<TResponse>(string relativeUrl, CurseForgeFilesRequest body, CancellationToken cancellationToken)
        {
            return await SendAsync<TResponse>(
                () => new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, relativeUrl)) { Content = JsonContent.Create(body, options: _javaScriptObjectNotationOptions) },
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false);
        }

        /// <summary>
        /// Sends a request until CurseForge answers it, retrying network errors, timeouts, rate limits and server errors. A sent request
        /// can't be sent again, so each attempt creates its own. Responses are read in full, so a connection lost mid-answer is retried too.
        /// </summary>
        private async Task<TResponse> SendAsync<TResponse>(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= UpstreamRequestAttempts; attempt++)
            {
                using var request = createRequest();

                request.Headers.Add(name: "x-api-key", _apiKey);

                string failure;
                bool transient;

                try
                {
                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadFromJsonAsync<TResponse>(_javaScriptObjectNotationOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                               ?? throw new InvalidOperationException(message: "CurseForge API returned an empty response");
                    }

                    failure = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                    transient = IsTransientStatus(response.StatusCode);
                }
                catch (HttpRequestException exception)
                {
                    failure = exception.Message;
                    transient = true;
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = $"timed out: {exception.Message}";
                    transient = true;
                }

                string requestDescription = $"CurseForge {request.Method} {request.RequestUri?.PathAndQuery}";

                if (!transient || attempt == UpstreamRequestAttempts)
                    throw CurseForgeInstallFailure(transient, $"{requestDescription} failed after {attempt} attempt(s): {failure}");

                await Console.Error.WriteLineAsync($"{requestDescription} failed on attempt {attempt}/{UpstreamRequestAttempts}: {failure}").ConfigureAwait(continueOnCapturedContext: false);
                await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
            }

            throw new InvalidOperationException(message: "CurseForge request attempts were exhausted");
        }
    }

    private record CurseForgeApiListResponse<TResponse>(List<TResponse>? Data);

    private record CurseForgeApiResponse<TResponse>(TResponse? Data);

    private record CurseForgeFile(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("modId")] int ModId,
        string FileName,
        [property: JsonPropertyName("downloadUrl")] string? DownloadUrl,
        [property: JsonPropertyName("hashes")] List<CurseForgeFileHash>? Hashes
    );

    private record CurseForgeFilesRequest([property: JsonPropertyName("fileIds")] List<int> FileIds);

    private record CurseForgeManifest(CurseForgeMinecraft? Minecraft, string? Overrides, List<CurseForgeManifestFile>? Files);

    private record CurseForgeManifestFile([property: JsonPropertyName("fileID")] int? FileId, bool? Required);

    private record CurseForgeMinecraft(string? Version, List<CurseForgeModLoader>? ModLoaders);

    private record CurseForgeModLoader([property: JsonPropertyName("id")] string? Id, bool? Primary);

    private record CurseForgeProject([property: JsonPropertyName("id")] int Id, string? Slug);
}
