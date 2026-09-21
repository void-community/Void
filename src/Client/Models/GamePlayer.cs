namespace Void.Client.Models;

internal sealed record GamePlayer(string? Uuid, string? Name, Position Position, BodyRotation Body, HeadRotation Head);
