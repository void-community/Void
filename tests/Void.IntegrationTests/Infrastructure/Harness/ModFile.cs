namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>A mod file a server installed, which clients install too.</summary>
public sealed record ModFile(string FileName, byte[] Content);
