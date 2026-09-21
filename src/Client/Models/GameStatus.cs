namespace Void.Client;

/// <summary>
/// Immutable coordinator snapshot. The operation identifier lets callers distinguish completion of their accepted
/// command from a later command issued by another caller.
/// </summary>
internal sealed record GameStatus(
    GameState State,
    [property: System.Text.Json.Serialization.JsonPropertyName("operationId")] long OperationIdentifier,
    string? Operation,
    OperationState OperationState,
    [property: System.Text.Json.Serialization.JsonPropertyName("processId")] int? ProcessIdentifier,
    int? ExitCode,
    ServerAddress? Server,
    string? Message,
    string? Error,
    ClientFailure? Failure,
    IReadOnlyList<string> Warnings,
    DateTimeOffset UpdatedAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("sessionId")] Guid? SessionIdentifier = null
);
