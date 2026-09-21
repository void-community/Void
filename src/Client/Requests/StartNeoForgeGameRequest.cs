namespace Void.Client.Requests;

internal sealed record StartNeoForgeGameRequest(string? Version, string[]? Arguments, int? MemoryMb = null);
