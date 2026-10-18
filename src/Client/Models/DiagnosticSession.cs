using System.Text.Json.Serialization;

namespace Void.Client.Models;

internal sealed record DiagnosticSession(
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    string Launch,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    GameStatus? Status,
    ClientFailure? LastFailure,
    IReadOnlyList<string> Warnings
)
{
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl => $"/api/game/diagnostics/{SessionId}";
}
