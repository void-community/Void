namespace Void.Client.Requests;

internal sealed record StartModLoaderGameRequest(string? Version, string? LoaderVersion, string[]? Arguments, int? MemoryMb = null);
