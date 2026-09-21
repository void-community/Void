namespace Void.Client.Models;

internal sealed record ConnectGameResponse(ServerAddress Server, DateTimeOffset ConnectedAt);
