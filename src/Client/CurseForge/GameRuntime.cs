using System.Text.Json;
using System.Text.Json.Serialization;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    sealed class CurseForgeApiClient(HttpClient hypertextTransferProtocolClient, Uri baseUniformResourceIdentifier, string apiKey)
    {
        private readonly string _apiKey = apiKey;
        private readonly Uri _baseUniformResourceIdentifier = baseUniformResourceIdentifier;
        private readonly HttpClient _hypertextTransferProtocolClient = hypertextTransferProtocolClient;
        private readonly JsonSerializerOptions _javaScriptObjectNotationOptions = new(JsonSerializerDefaults.Web);

        public async Task<List<CurseForgeFile>> GetFilesAsync(List<int> fileIdentifiers, CancellationToken cancellationToken)
        {
            var response = await PostAsync<CurseForgeApiListResponse<CurseForgeFile>>(relativeUniformResourceLocator: "v1/mods/files", new CurseForgeFilesRequest(fileIdentifiers), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? [];
        }

        public async Task<CurseForgeFile> GetModFileAsync(int modIdentifier, int fileIdentifier, CancellationToken cancellationToken)
        {
            var response = await GetAsync<CurseForgeApiResponse<CurseForgeFile>>($"v1/mods/{modIdentifier}/files/{fileIdentifier}", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? throw new InvalidOperationException($"CurseForge file not found: mod {modIdentifier}, file {fileIdentifier}");
        }

        public async Task<string?> GetModFileDownloadUniformResourceLocatorAsync(int modIdentifier, int fileIdentifier, CancellationToken cancellationToken)
        {
            var response = await GetAsync<CurseForgeApiResponse<string?>>($"v1/mods/{modIdentifier}/files/{fileIdentifier}/download-url", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data;
        }

        public async Task<List<CurseForgeProject>> SearchModsAsync(int gameIdentifier, string slug, CancellationToken cancellationToken)
        {
            var slugQuery = Uri.EscapeDataString(slug);
            var response = await GetAsync<CurseForgeApiListResponse<CurseForgeProject>>($"v1/mods/search?gameIdentifier={gameIdentifier}&slug={slugQuery}", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return response.Data ?? [];
        }

        private async Task<TResponse> GetAsync<TResponse>(string relativeUniformResourceLocator, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUniformResourceIdentifier, relativeUniformResourceLocator));

            return await SendAsync<TResponse>(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }

        private async Task<TResponse> PostAsync<TResponse>(string relativeUniformResourceLocator, CurseForgeFilesRequest body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUniformResourceIdentifier, relativeUniformResourceLocator))
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
        [property: JsonPropertyName("id")] int Identifier,
        [property: JsonPropertyName("modId")] int ModIdentifier,
        string FileName,
        [property: JsonPropertyName("downloadUrl")] string? DownloadUniformResourceLocator
    );

    record CurseForgeFilesRequest([property: JsonPropertyName("fileIds")] List<int> FileIdentifiers);

    record CurseForgeManifest(CurseForgeMinecraft? Minecraft, string? Overrides, List<CurseForgeManifestFile>? Files);

    record CurseForgeManifestFile([property: JsonPropertyName("fileID")] int? FileIdentifier, bool? Required);

    record CurseForgeMinecraft(string? Version, List<CurseForgeModLoader>? ModLoaders);

    record CurseForgeModLoader([property: JsonPropertyName("id")] string? Identifier, bool? Primary);

    record CurseForgeProject([property: JsonPropertyName("id")] int Identifier, string? Slug);
}
