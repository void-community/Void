namespace Void.Client;

internal sealed record StartNeoForgeGameRequest(string? Version, string[]? Arguments, int? MemoryMb = null);
