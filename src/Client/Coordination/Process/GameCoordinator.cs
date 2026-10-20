using Void.Client.Failures;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    // Session diagnostics retain this many status warnings.
    private const int MaximumStatusWarnings = 8;

    private static string? FindStartProblem(StartMessage message)
    {
        var neoForgeRequest = message.NeoForgeRequest;
        var curseForgeRequest = message.CurseForgeRequest;
        bool maximumMinecraftVersionConflicts = !string.IsNullOrWhiteSpace(neoForgeRequest?.Version) && !string.IsNullOrWhiteSpace(neoForgeRequest?.MaximumMinecraftVersion);
        bool modpackMissing = string.IsNullOrWhiteSpace(curseForgeRequest?.Slug) || curseForgeRequest?.FileId <= 0;
        bool versionMissing = string.IsNullOrWhiteSpace(message.Request?.Version ?? message.ModLoaderRequest?.Version);

        return message.Kind switch
        {
            "start-neoforge" => maximumMinecraftVersionConflicts ? "maximumMinecraftVersion can only be used when version is omitted" : null,
            "start-curseforge" => modpackMissing ? "slug and a positive fileId are required" : null,
            _ => versionMissing ? "version is required" : null
        };
    }

    private static bool IsMaximumHeapArgument(string argument)
    {
        return argument.StartsWith(value: "-Xmx", StringComparison.Ordinal)
               || argument.StartsWith(value: "--jvm-arg=-Xmx", StringComparison.Ordinal);
    }

    private string? FindMemoryProblem(int? memoryMb, string[] arguments)
    {
        if (memoryMb is not { } heapMb)
            return null;

        if (heapMb <= 0)
            return "memoryMb must be a positive integer";

        if (arguments.Any(IsMaximumHeapArgument))
            return "memoryMb cannot be combined with an -Xmx JVM argument";

        // The container's memory also holds the rest of the game and the client, so a heap that large ends with the game killed.
        return runtime.ReadMemoryLimitMb() is { } limitMb && heapMb >= limitMb
            ? $"memoryMb must be lower than the container's memory limit of {limitMb} MiB"
            : null;
    }

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
        GameProcessExitException processFailure = new(exited.WasOutOfMemoryKilled, exited.MemoryMb, exited.CrashCause);

        await StopGameMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);
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
                Message = exited.CrashCause is null ? "Game exited without a stop request" : "Game crashed",
                Error = processFailure.Message,
                Failure = processFailure.Failure,
                // The failure explains the end of the game, including the end of any connection.
                Disconnect = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (_sessionId is { } sessionId)
            await (diagnostics?.CompleteAsync(sessionId, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
    }
}
