using Void.Client.Models;
using Void.Client.States;

namespace Void.Client;

internal sealed partial class GameCoordinator
{
    private static readonly TimeSpan ConnectionCheckInterval = TimeSpan.FromSeconds(seconds: 1);

    private CancellationTokenSource? _connectionMonitor;

    private async Task HandleConnectionLostAsync(ConnectionLost lost)
    {
        if (lost.Monitor != _connectionMonitor)
            return;

        await StopConnectionMonitorAsync().ConfigureAwait(continueOnCapturedContext: false);
        _connectedResponse = null;
        await PublishAsync(
            Status with { State = GameState.Ready, Server = null, Message = $"Disconnected from {lost.Server.Host}:{lost.Server.Port}: {lost.Reason}", UpdatedAt = DateTimeOffset.UtcNow }
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

    /// <summary>
    /// Watches a connected game until it leaves the server, because the game can be kicked, time out or lose its connection without any API call.
    /// </summary>
    private void StartConnectionMonitor(RunningGame game, ServerAddress server)
    {
        CancellationTokenSource monitor = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _connectionMonitor = monitor;
        Own(MonitorConnectionAsync(game, server, monitor, monitor.Token));
    }

    private async Task StopConnectionMonitorAsync()
    {
        if (_connectionMonitor is null)
            return;

        await _connectionMonitor.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);
        _connectionMonitor.Dispose();
        _connectionMonitor = null;
    }
}
