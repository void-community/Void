namespace Void.Client;

internal sealed record StartCurseForgeGameRequest(
    string? Slug,
    [property: System.Text.Json.Serialization.JsonPropertyName("fileId")] int FileIdentifier,
    string[]? Arguments,
    int? MemoryMb = null
);
