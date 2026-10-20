namespace Void.Client.Models;

/// <summary>Why the game isn't connected: the last connect that failed, or the server the game left.</summary>
internal sealed record GameDisconnect(ServerAddress Server, string Code, string Stage, string Message, DateTimeOffset DisconnectedAt);
