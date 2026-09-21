namespace Void.Client.Models;

internal sealed record GamePlayers(GamePlayer Local, IReadOnlyList<RemoteGamePlayer> Remote);
