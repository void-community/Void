namespace Void.Client;

internal sealed record StartGameRequest(string? Version, string[]? Arguments, int? MemoryMb = null);
