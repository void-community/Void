using Void.Client.Failures;
using Void.Client.Models;

namespace Void.Client;

internal sealed partial class GameRuntime
{
    private async Task DisconnectThroughAgentAsync(RunningGame game, CancellationToken cancellationToken)
    {
        var response = await QueryAgentAsync(game, command: "disconnect", cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        // A game that already left the server, such as one that was kicked a moment ago, has nothing to close.
        if (response?.Status is "disconnected")
            return;

        if (response?.Status is not "ok")
        {
            throw new GameClientException(
                code: "client.disconnect.failed",
                operation: "disconnect",
                response?.Stage ?? "agent.disconnect",
                response?.Message ?? "The Minecraft agent returned no diagnostic"
            );
        }

        // The game closes its connection at once, and has left the server once the agent reports the connection closed.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromMilliseconds(DisconnectTimeoutMilliseconds));

        try
        {
            while (await ReadDisconnectReasonAsync(game, timeout.Token).ConfigureAwait(continueOnCapturedContext: false) is null)
                await Task.Delay(DisconnectCheckIntervalMilliseconds, timeout.Token).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The game was still connected {DisconnectTimeoutMilliseconds / 1000} seconds after closing its connection");
        }

        await Console.Error.WriteLineAsync(value: "Minecraft agent closed the connection to the server").ConfigureAwait(continueOnCapturedContext: false);
    }
}
