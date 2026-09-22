using Void.Client.Models;
using Void.Client.Requests;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private sealed record ConnectCompleted(long OperationId, ServerAddress Server, Exception? Error, bool Canceled, CancellationTokenSource Cancellation) : Message;
    private sealed record ConnectMessage(ConnectGameRequest Request, TaskCompletionSource<ConnectGameResponse> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record ConnectWaiter(TaskCompletionSource<ConnectGameResponse> Completion, CancellationTokenRegistration CancellationRegistration);
    private sealed record ConnectWaiterCanceled(TaskCompletionSource<ConnectGameResponse> Completion, CancellationToken CancellationToken) : Message;
    private abstract record Message;
    private sealed record OptionsMessage(string Options, TaskCompletionSource Completion, CancellationToken RequestCancellation) : Message;
    private sealed record PlayersMessage(TaskCompletionSource<GamePlayers> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record ProcessExited(int ProcessId, int ExitCode, bool WasOutOfMemoryKilled, int? MemoryMb) : Message;
    private sealed record ScreenshotCompleted(
        long OperationId,
        byte[]? Image,
        Exception? Error,
        bool Canceled,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<byte[]> Completion
    ) : Message;
    private sealed record ScreenshotMessage(TaskCompletionSource<byte[]> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record SendChatMessage(SendChatRequest Request, TaskCompletionSource Completion, CancellationToken RequestCancellation) : Message;
    private sealed record StartCompleted(long OperationId, string Kind, RunningGame? Game, Exception? Error, bool Canceled, CancellationTokenSource Cancellation) : Message;
    private sealed record StartMessage(
        string Kind,
        StartGameRequest? Request,
        StartNeoForgeGameRequest? NeoForgeRequest,
        StartCurseForgeGameRequest? CurseForgeRequest,
        TaskCompletionSource<GameStatus> Completion
    ) : Message;
    private sealed record StopCompleted(
        long OperationId,
        StopMode Mode,
        Exception? Error,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<StopGameResponse> Completion
    ) : Message;
    private sealed record StopMessage(TaskCompletionSource<StopGameResponse> Completion) : Message;
    private sealed record VoidOperationCompleted(
        long OperationId,
        string Kind,
        Exception? Error,
        bool Canceled,
        CancellationTokenSource Cancellation,
        TaskCompletionSource Completion
    ) : Message;
}
