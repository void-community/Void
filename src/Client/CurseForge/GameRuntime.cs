using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    sealed class CurseForgeApiClient(HttpClient hypertextTransferProtocolClient, Uri baseUri, string apiKey)
    {
        private readonly string _apiKey = apiKey;
        private readonly Uri _baseUri = baseUri;
        private readonly HttpClient _hypertextTransferProtocolClient = hypertextTransferProtocolClient;
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

        public async Task<string?> GetModFileDownloadUrlAsync(int modId, int fileId, CancellationToken cancellationToken)
        {
            var response = await GetAsync<CurseForgeApiResponse<string?>>($"v1/mods/{modId}/files/{fileId}/download-url", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data;
        }

        public async Task<List<CurseForgeProject>> SearchModsAsync(int gameId, string slug, CancellationToken cancellationToken)
        {
            var slugQuery = Uri.EscapeDataString(slug);
            var response = await GetAsync<CurseForgeApiListResponse<CurseForgeProject>>($"v1/mods/search?gameId={gameId}&slug={slugQuery}", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? [];
        }

        private async Task<TResponse> GetAsync<TResponse>(string relativeUrl, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativeUrl));

            return await SendAsync<TResponse>(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        private async Task<TResponse> PostAsync<TResponse>(string relativeUrl, CurseForgeFilesRequest body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, relativeUrl))
            {
                Content = JsonContent.Create(body, options: _javaScriptObjectNotationOptions)
            };

            return await SendAsync<TResponse>(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ReturnedValue.Consume(request.Headers.TryAddWithoutValidation(name: "x-api-key", _apiKey));

            using var response = await _hypertextTransferProtocolClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            var successfulResponse = response.EnsureSuccessStatusCode();

            return await successfulResponse.Content.ReadFromJsonAsync<TResponse>(_javaScriptObjectNotationOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)
                   ?? throw new InvalidOperationException(message: "CurseForge API returned an empty response");
        }
    }

    record CurseForgeApiListResponse<TResponse>(List<TResponse>? Data);

    record CurseForgeApiResponse<TResponse>(TResponse? Data);

    record CurseForgeFile(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("modId")] int ModId,
        string FileName,
        [property: JsonPropertyName("downloadUrl")] string? DownloadUrl
    );

    record CurseForgeFilesRequest([property: JsonPropertyName("fileIds")] List<int> FileIds);

    record CurseForgeManifest(CurseForgeMinecraft? Minecraft, string? Overrides, List<CurseForgeManifestFile>? Files);

    record CurseForgeManifestFile([property: JsonPropertyName("fileID")] int? FileId, bool? Required);

    record CurseForgeMinecraft(string? Version, List<CurseForgeModLoader>? ModLoaders);

    record CurseForgeModLoader([property: JsonPropertyName("id")] string? Id, bool? Primary);

    record CurseForgeProject([property: JsonPropertyName("id")] int Id, string? Slug);
}
