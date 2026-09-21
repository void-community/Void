namespace Void.Client;

internal sealed record GamePlayers(GamePlayer Local, IReadOnlyList<RemoteGamePlayer> Remote);
