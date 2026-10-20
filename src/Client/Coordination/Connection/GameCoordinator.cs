using Void.Client.Models;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromSeconds(seconds: 1);

    private CancellationTokenSource? _gameMonitor;

    private async Task HandleConnectionLostAsync(ConnectionLost lost)
    {
        if (lost.Monitor != _gameMonitor)
            return;

        await StopGameMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);
        _connectedResponse = null;
        await PublishAsync(
            Status with
            {
                State = GameState.Ready,
                Server = null,
                Message = $"Disconnected from {lost.Server.Host}:{lost.Server.Port}: {lost.Reason}",
                Disconnect = new GameDisconnect(lost.Server, Code: "client.connection.lost", Stage: "connection.closed", lost.Reason, DateTimeOffset.UtcNow),
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task HandleMenuShownAsync(MenuShown shown)
    {
        // A late answer from an earlier game, or from a game that already connected, must not change this one.
        if (shown.Monitor != _gameMonitor || Status.State is not GameState.Loading)
            return;

        await StopGameMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);

        IReadOnlyList<string> warnings = shown.Problem is null || Status.Warnings.Contains(shown.Problem) || Status.Warnings.Count >= MaximumStatusWarnings
            ? Status.Warnings
            : [.. Status.Warnings, shown.Problem];

        await PublishAsync(
            Status with
            {
                State = GameState.Ready,
                // A running operation, such as a connect started while the game loaded, keeps describing itself.
                Message = _activeCancellation is null ? "Game menu is ready" : Status.Message,
                Warnings = warnings,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task MonitorConnectionAsync(RunningGame game, ServerAddress server, CancellationTokenSource monitor, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = runtime.ReadDisconnectReasonAsync(game, cancellationToken);
            await ((Task)read).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            // The agent may not answer every check, such as while the game is busy, so only a reported disconnect ends the connection.
            if (read.IsCompletedSuccessfully && await read.ConfigureAwait(continueOnCapturedContext: false) is { } reason)
            {
                await WriteCompletionAsync(new ConnectionLost(monitor, server, reason)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            await Task.Delay(ConnectionCheckInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task MonitorMenuAsync(RunningGame game, CancellationTokenSource monitor, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = runtime.ReadMenuAsync(game, cancellationToken);
            await ((Task)read).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            // The agent may not answer while the game loads, so only a reported menu, or a reason it can't tell, ends the wait.
            if (read.IsCompletedSuccessfully && await read.ConfigureAwait(continueOnCapturedContext: false) is { } menu)
            {
                await WriteCompletionAsync(new MenuShown(monitor, menu.Problem)).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }

            await Task.Delay(ConnectionCheckInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>
    /// Watches a connected game until it leaves the server, because the game can be kicked, time out or lose its connection without any API call.
    /// </summary>
    private void StartConnectionMonitor(RunningGame game, ServerAddress server)
    {
        CancellationTokenSource monitor = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _gameMonitor = monitor;
        Own(MonitorConnectionAsync(game, server, monitor, monitor.Token));
    }

    /// <summary>
    /// Watches a game whose window opened until it shows a menu, because a window, such as NeoForge's loading window, appears long before
    /// the game can be used.
    /// </summary>
    private void StartMenuMonitor(RunningGame game)
    {
        CancellationTokenSource monitor = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _gameMonitor = monitor;
        Own(MonitorMenuAsync(game, monitor, monitor.Token));
    }

    private async Task StopGameMonitorAsync()
    {
        if (_gameMonitor is null)
            return;

        await _gameMonitor.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
        _gameMonitor.Dispose();
        _gameMonitor = null;
    }
}
