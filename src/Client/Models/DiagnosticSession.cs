namespace Void.Client;

internal sealed record DiagnosticSession(
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionId")] Guid SessionId,
    string Launch,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    GameStatus? Status,
    ClientFailure? LastFailure,
    IReadOnlyList<string> Warnings
)
{
    [System.Text.Json.Serialization.JsonPropertyName("downloadUrl")]
    public string DownloadUrl => $"/api/game/diagnostics/{SessionId}";
}
