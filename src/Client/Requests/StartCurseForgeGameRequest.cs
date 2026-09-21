namespace Void.Client.Requests;

internal sealed record StartCurseForgeGameRequest(
    string? Slug,
    [property: System.Text.Json.Serialization.JsonPropertyName("fileId")] int FileId,
    string[]? Arguments,
    int? MemoryMb = null
);
