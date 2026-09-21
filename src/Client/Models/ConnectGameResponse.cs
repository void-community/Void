namespace Void.Client;

internal sealed record ConnectGameResponse(ServerAddress Server, DateTimeOffset ConnectedAt);
