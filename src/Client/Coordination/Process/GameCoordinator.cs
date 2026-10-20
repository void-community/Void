using Void.Client.Failures;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    // Session diagnostics retain this many status warnings.
    private const int MaximumStatusWarnings = 8;

    private async Task HandleGameWarningAsync(GameWarning warning)
    {
        bool fromCurrentGame = _game?.Process.Id == warning.ProcessId;
        bool alreadyReported = Status.Warnings.Contains(warning.Text);

        if (!fromCurrentGame || alreadyReported || Status.Warnings.Count >= MaximumStatusWarnings)
            return;

        await PublishAsync(Status with { Warnings = [.. Status.Warnings, warning.Text], UpdatedAt = DateTimeOffset.UtcNow }).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task HandleProcessExitedAsync(ProcessExited exited)
    {
        if (_game?.Process.Id != exited.ProcessId)
            return;

        // Stop completion owns final state and disposal so an expected exit cannot race it into a false failure.
        if (Status.State is GameState.Stopping)
        {
            await PublishAsync(Status with { ProcessId = null, ExitCode = exited.ExitCode, UpdatedAt = DateTimeOffset.UtcNow }).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        bool processExitedDuringOperation = _activeCancellation is not null || _connectWaiters.Count is not 0;

        // Only stop ends a game on purpose. The exit code is the launcher's, which is 0 whatever the game did, so a quit, a crash and a
        // kill can't be told apart and every exit without a stop request is a failure.
        GameProcessExitException processFailure = new(exited.WasOutOfMemoryKilled, exited.MemoryMb);

        await StopConnectionMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);
        _game.Dispose();
        _game = null;
        _connectedResponse = null;
        Volatile.Write(ref _processExitFailure, processFailure);

        if (_activeCancellation is not null)
            await _activeCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);

        FailConnectWaiters(processFailure);

        _activeCancellation = null;
        await PublishAsync(
            Status with
            {
                State = GameState.Failed,
                // Without a running operation, the operation fields still describe the last one, which had finished before the exit.
                OperationState = processExitedDuringOperation ? OperationState.Failed : Status.OperationState,
                ProcessId = null,
                ExitCode = exited.ExitCode,
                Server = null,
                Message = "Game exited without a stop request",
                Error = processFailure.Message,
                Failure = processFailure.Failure,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (_sessionId is { } sessionId)
            await (diagnostics?.CompleteAsync(sessionId, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
    }
}
