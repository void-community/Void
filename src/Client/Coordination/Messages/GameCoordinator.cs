namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private sealed record ConnectCompleted(long OperationIdentifier, ServerAddress Server, Exception? Error, bool Canceled, CancellationTokenSource Cancellation) : Message;
    private sealed record ConnectMessage(ConnectGameRequest Request, TaskCompletionSource<ConnectGameResponse> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record ConnectWaiter(TaskCompletionSource<ConnectGameResponse> Completion, CancellationTokenRegistration CancellationRegistration);
    private sealed record ConnectWaiterCanceled(TaskCompletionSource<ConnectGameResponse> Completion, CancellationToken CancellationToken) : Message;
    private abstract record Message;
    private sealed record OptionsMessage(string Options, TaskCompletionSource<bool> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record PlayersMessage(TaskCompletionSource<GamePlayers> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record ProcessExited(int ProcessIdentifier, int ExitCode, bool WasOutOfMemoryKilled, int? MemoryMb) : Message;
    private sealed record ScreenshotCompleted(
        long OperationIdentifier,
        byte[]? Image,
        Exception? Error,
        bool Canceled,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<byte[]> Completion
    ) : Message;
    private sealed record ScreenshotMessage(TaskCompletionSource<byte[]> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record SendChatMessage(SendChatRequest Request, TaskCompletionSource<bool> Completion, CancellationToken RequestCancellation) : Message;
    private sealed record StartCompleted(long OperationIdentifier, string Kind, RunningGame? Game, Exception? Error, bool Canceled, CancellationTokenSource Cancellation) : Message;
    private sealed record StartMessage(
        string Kind,
        StartGameRequest? Request,
        StartNeoForgeGameRequest? NeoForgeRequest,
        StartCurseForgeGameRequest? CurseForgeRequest,
        TaskCompletionSource<GameStatus> Completion
    ) : Message;
    private sealed record StopCompleted(
        long OperationIdentifier,
        StopMode Mode,
        Exception? Error,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<StopGameResponse> Completion
    ) : Message;
    private sealed record StopMessage(TaskCompletionSource<StopGameResponse> Completion) : Message;
    private sealed record VoidOperationCompleted(
        long OperationIdentifier,
        string Kind,
        Exception? Error,
        bool Canceled,
        CancellationTokenSource Cancellation,
        TaskCompletionSource<bool> Completion
    ) : Message;
}
