namespace Void.Client.Requests;

internal sealed record StartGameRequest(string? Version, string[]? Arguments, int? MemoryMb = null);
