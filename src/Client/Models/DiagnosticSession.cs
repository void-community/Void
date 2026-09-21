namespace Void.Client;

internal sealed record DiagnosticSession(
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionId")] Guid SessionIdentifier,
    string Launch,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    GameStatus? Status,
    ClientFailure? LastFailure,
    IReadOnlyList<string> Warnings
)
{
    [System.Text.Json.Serialization.JsonPropertyName("downloadUrl")]
    public string DownloadUniformResourceLocator => $"/api/game/diagnostics/{SessionIdentifier}";
}
