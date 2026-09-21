namespace Void.Client.Models;

internal sealed record RemoteGamePlayer(string? Uuid, string? Name, Position Position, BodyRotation Body, HeadRotation Head);
