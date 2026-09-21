namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private async Task MonitorProcessAsync(RunningGame game)
    {
        await game.Process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(
            new ProcessExited(game.Process.Identifier, game.Process.ExitCode ?? -1, game.Process.WasOutOfMemoryKilled, game.Process.MemoryMb)
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task<(Exception? Error, bool Canceled)> ObserveAsync(Task operation, CancellationTokenSource cancellation, long operationIdentifier)
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

        await CaptureFailureAsync(operationIdentifier).ConfigureAwait(continueOnCapturedContext: false);

        return (exception, false);
    }

    private async Task ObserveConnectAsync(long operationIdentifier, ServerAddress server, Task operation, CancellationTokenSource cancellation)
    {
        var (error, canceled) = await ObserveAsync(operation, cancellation, operationIdentifier).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new ConnectCompleted(operationIdentifier, server, error, canceled, cancellation)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveScreenshotAsync(long operationIdentifier, Task<byte[]> operation, CancellationTokenSource cancellation, TaskCompletionSource<byte[]> completion)
    {
        await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
        {
            var image = await operation.ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new ScreenshotCompleted(operationIdentifier, image, Error: null, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        var exception = GetTaskException(operation);

        if (operation.IsCanceled && cancellation.IsCancellationRequested)
        {
            if (Volatile.Read(ref _processExitFailure) is { } processExitFailure)
                await WriteCompletionAsync(new ScreenshotCompleted(operationIdentifier, Image: null, processExitFailure, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
            else
                await WriteCompletionAsync(new ScreenshotCompleted(operationIdentifier, Image: null, exception, Canceled: true, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        await WriteCompletionAsync(new ScreenshotCompleted(operationIdentifier, Image: null, exception, Canceled: false, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveStartAsync(long operationIdentifier, string kind)
    {
        if (!_startObservations.TryRemove(operationIdentifier, out var observation))
            throw new InvalidOperationException($"Start observation {operationIdentifier} is not registered");

        using (observation.DiagnosticContext)
        {
            var operation = observation.Operation;
            var cancellation = observation.Cancellation;
            await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (operation.IsCompletedSuccessfully)
            {
                var game = await operation.ConfigureAwait(continueOnCapturedContext: false);
                await WriteCompletionAsync(new StartCompleted(operationIdentifier, kind, game, Error: null, Canceled: false, cancellation)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            var exception = GetTaskException(operation);

            if (operation.IsCanceled && cancellation.IsCancellationRequested)
            {
                await WriteCompletionAsync(new StartCompleted(operationIdentifier, kind, Game: null, exception, Canceled: true, cancellation)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            await CaptureFailureAsync(operationIdentifier).ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new StartCompleted(operationIdentifier, kind, Game: null, exception, Canceled: false, cancellation)).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private async Task ObserveStopAsync(
        long operationIdentifier,
        Task<StopMode> operation,
        CancellationTokenSource cancellation,
        TaskCompletionSource<StopGameResponse> completion
    )
    {
        await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
        {
            var mode = await operation.ConfigureAwait(continueOnCapturedContext: false);
            await WriteCompletionAsync(new StopCompleted(operationIdentifier, mode, Error: null, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        await WriteCompletionAsync(new StopCompleted(operationIdentifier, Mode: default, GetTaskException(operation), cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task ObserveVoidOperationAsync(
        long operationIdentifier,
        string kind,
        Task operation,
        CancellationTokenSource cancellation,
        TaskCompletionSource<bool> completion
    )
    {
        var (error, canceled) = await ObserveAsync(operation, cancellation, operationIdentifier).ConfigureAwait(continueOnCapturedContext: false);
        await WriteCompletionAsync(new VoidOperationCompleted(operationIdentifier, kind, error, canceled, cancellation, completion)).ConfigureAwait(continueOnCapturedContext: false);
    }

    private void Own(Task task)
    {
        _ownedTasks.Add(task);
    }

    private async Task PublishAsync(GameStatus status)
    {
        status = status with { SessionIdentifier = _sessionIdentifier };
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
