using System.Text.Json.Serialization;

namespace Void.Client.Requests;

internal sealed record StartCurseForgeGameRequest(string? Slug, [property: JsonPropertyName("fileId")] int FileId, string[]? Arguments, int? MemoryMb = null);
