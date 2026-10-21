using Void.Client.Models;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private async Task HandleDisconnectAsync(DisconnectMessage message)
    {
        if (_game is null || Status.State is not GameState.Connected || Status.Server is not { } server)
        {
            message.Completion.SetException(Conflict(message: "A connected game is required before disconnecting"));

            return;
        }

        if (_activeCancellation is not null)
        {
            message.Completion.SetException(Conflict(message: "Another game operation is running"));

            return;
        }

        // The game leaves because it was asked to, so the connection monitor must not report the connection as lost meanwhile.
        await StopGameMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);

        var (operationId, cancellation) = await BeginConfirmedOperationAsync(operation: "disconnect", message.RequestCancellation).ConfigureAwait(continueOnCapturedContext: false);
        Own(
            ObserveDisconnectAsync(operationId, server, runtime.DisconnectAsync(_game, cancellation.Token), cancellation, message.Completion)
        );
    }

    private async Task HandleDisconnectCompletedAsync(DisconnectCompleted completed)
    {
        completed.Cancellation.Dispose();

        if (completed.OperationId != Status.OperationId || completed.Error is not null)
        {
            bool operationCompleted = await CompleteConfirmedOperationAsync(
                completed.OperationId,
                operation: "disconnect",
                completed.Error,
                completed.Canceled,
                completed.Completion.SetCanceled,
                completed.Completion.SetException
            ).ConfigureAwait(continueOnCapturedContext: false);

            // A game that is still connected is watched again, so it still returns to ready if it leaves the server later.
            if (!operationCompleted && _game is not null && _gameMonitor is null && Status.State is GameState.Connected)
                StartConnectionMonitor(_game, completed.Server);

            return;
        }

        _activeCancellation = null;
        _connectedResponse = null;
        await PublishAsync(
            Status with
            {
                State = GameState.Ready,
                Server = null,
                OperationState = OperationState.Succeeded,
                Message = $"Disconnected from {completed.Server.Host}:{completed.Server.Port}",
                Error = null,
                Failure = null,
                Disconnect = new GameDisconnect(
                    completed.Server,
                    Code: "client.disconnect.requested",
                    Stage: "connection.closed",
                    Message: "The game left the server on request",
                    DateTimeOffset.UtcNow
                ),
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);

        completed.Completion.SetResult(Status);
    }

    private async Task ObserveDisconnectAsync(
        long operationId,
        ServerAddress server,
        Task operation,
        CancellationTokenSource cancellation,
        TaskCompletionSource<GameStatus> completion
    )
    {
        var (error, canceled) = await ObserveAsync(operation, cancellation, operationId).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new DisconnectCompleted(operationId, server, error, canceled, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }
}
