namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private async Task MonitorProcessAsync(RunningGame game)
    {
        await game.Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new ProcessExited(game.Process.Id, game.Process.ExitCode ?? -1, game.Process.WasOutOfMemoryKilled, game.Process.MemoryMb)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task<(Exception? Error, bool Canceled)> ObserveAsync(Task operation, CancellationTokenSource cancellation, long operationId)
    {
        await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
            return (null, false);

        var exception = GetTaskException(operation);

        if (operation.IsCanceled && cancellation.IsCancellationRequested)
        {
            return Volatile.Read(ref _processExitFailure) is { } processExitFailure
                ? (processExitFailure, false)
                : (exception, true);
        }

        await CaptureFailureAsync(operationId).ConfigureAwait(continueOnCapturedContext: false);

        return (exception, false);
    }

    private async Task ObserveConnectAsync(long operationId, ServerAddress server, Task operation, CancellationTokenSource cancellation)
    {
        var (error, canceled) = await ObserveAsync(operation, cancellation, operationId).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new ConnectCompleted(operationId, server, error, canceled, cancellation)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveScreenshotAsync(long operationId, Task<byte[]> operation, CancellationTokenSource cancellation, TaskCompletionSource<byte[]> completion)
    {
        await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
        {
            var image = await operation.ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new ScreenshotCompleted(operationId, image, Error: null, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        var exception = GetTaskException(operation);

        if (operation.IsCanceled && cancellation.IsCancellationRequested)
        {
            if (Volatile.Read(ref _processExitFailure) is { } processExitFailure)
                await WriteCompletionAsync(new ScreenshotCompleted(operationId, Image: null, processExitFailure, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
            else
                await WriteCompletionAsync(new ScreenshotCompleted(operationId, Image: null, exception, Canceled: true, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        await WriteCompletionAsync(new ScreenshotCompleted(operationId, Image: null, exception, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveStartAsync(long operationId, string kind)
    {
        if (!_startObservations.TryRemove(operationId, out var observation))
            throw new InvalidOperationException($"Start observation {operationId} is not registered");

        using (observation.DiagnosticContext)
        {
            var operation = observation.Operation;
            var cancellation = observation.Cancellation;
            await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (operation.IsCompletedSuccessfully)
            {
                var game = await operation.ConfigureAwait(continueOnCapturedContext: false);
                await WriteCompletionAsync(new StartCompleted(operationId, kind, game, Error: null, Canceled: false, cancellation)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            var exception = GetTaskException(operation);

            if (operation.IsCanceled && cancellation.IsCancellationRequested)
            {
                await WriteCompletionAsync(new StartCompleted(operationId, kind, Game: null, exception, Canceled: true, cancellation)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            await CaptureFailureAsync(operationId).ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new StartCompleted(operationId, kind, Game: null, exception, Canceled: false, cancellation)).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private async Task ObserveStopAsync(
        long operationId,
        Task<StopMode> operation,
        CancellationTokenSource cancellation,
        TaskCompletionSource<StopGameResponse> completion
    )
    {
        await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
        {
            var mode = await operation.ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new StopCompleted(operationId, mode, Error: null, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        await WriteCompletionAsync(new StopCompleted(operationId, Mode: default, GetTaskException(operation), cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveVoidOperationAsync(long operationId, string kind, Task operation, CancellationTokenSource cancellation, TaskCompletionSource<bool> completion)
    {
        var (error, canceled) = await ObserveAsync(operation, cancellation, operationId).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new VoidOperationCompleted(operationId, kind, error, canceled, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private void Own(Task task)
    {
        _ownedTasks.Add(task);
    }

    private async Task PublishAsync(GameStatus status)
    {
        status = status with { SessionId = _sessionId };
        await ((diagnostics?.RecordAsync(status, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false));
        Volatile.Write(ref _status, status);
    }

    private async Task WriteCompletionAsync(Message message)
    {
        var canWrite = await _messages.Writer.WaitToWriteAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        var messageWritten = canWrite && _messages.Writer.TryWrite(message);

        if (!messageWritten)
            LogRejectedMessage(logger, message.GetType().Name, arg3: null);
    }
}
