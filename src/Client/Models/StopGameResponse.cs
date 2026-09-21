using Void.Client.States;

namespace Void.Client.Models;

internal sealed record StopGameResponse(StopMode Mode, GameStatus Status);
