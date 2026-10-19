using System.CommandLine;
using Microsoft.Extensions.Logging;
using Void.Minecraft.Links.Extensions;
using Void.Minecraft.Network;
using Void.Minecraft.Players.Extensions;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Player;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Services.Lifecycle;

namespace Void.Proxy.Plugins.ModsSupport.SlowJoin.Services;

/// <summary>
/// Answers a backend's keep-alives for a player that is still loading after joining it, until the player has answered several
/// keep-alives of the proxy's own in a row promptly. From then on the proxy forwards keep-alives between the backend and the player.
/// </summary>
public class SlowJoinService(Plugin plugin, Settings settings, IConsoleService console) : IEventListener, IDisposable
{
    private readonly Lock _lock = new();
    private SlowJoinGrace? _grace;
    private CancellationTokenSource? _timeoutCancellation;
    private Task? _timeoutTask;

    /// <summary>
    /// Chooses a value in seconds: the program argument when given, otherwise the setting, and at least one second.
    /// </summary>
    public static TimeSpan GetTimeout(int? argumentSeconds, int settingSeconds)
    {
        return TimeSpan.FromSeconds(GetCount(argumentSeconds, settingSeconds));
    }

    /// <summary>
    /// Chooses a value: the program argument when given, otherwise the setting, and at least one.
    /// </summary>
    public static int GetCount(int? argument, int setting)
    {
        return Math.Max(argument ?? setting, 1);
    }

    [Subscribe]
    public void OnPlayerJoinedServer(PlayerJoinedServerEvent @event)
    {
        if (!settings.Enabled || !@event.Player.IsMinecraft)
            return;

        var grace = new SlowJoinGrace(
            @event.Link,
            GetTimeout(GetArgument(plugin.SlowJoinTimeoutOption), settings.FirstKeepAliveTimeout),
            GetCount(GetArgument(plugin.SlowJoinPromptAnswersOption), settings.PromptAnswers),
            GetTimeout(GetArgument(plugin.SlowJoinPromptAnswerTimeOption), settings.PromptAnswerTime));

        ReplaceGrace(grace);
        @event.Player.Logger.LogDebug("Answering Keep Alives from {Server} until the player has finished loading", @event.Server);
    }

    [Subscribe(PostOrder.First)]
    public async ValueTask OnMessageReceived(MessageReceivedEvent @event, CancellationToken cancellationToken)
    {
        SlowJoinGrace? grace;

        lock (_lock)
            grace = _grace;

        if (grace is null || grace.IsOver || !ReferenceEquals(grace.Link, @event.Link))
            return;

        switch (@event.Message)
        {
            case KeepAliveRequestPacket keepAliveRequest:
                await AnswerAsync(@event, grace, keepAliveRequest, cancellationToken);
                break;
            case KeepAliveResponsePacket keepAliveResponse when grace.TryAnswer(keepAliveResponse.Id, DateTimeOffset.UtcNow, out var waited, out int answeredPromptly):
                @event.Cancel();
                CancelTimeout();

                if (grace.IsOver)
                    @event.Player.Logger.LogInformation("Player answered {Count} Keep Alives in a row on {Server} within {PromptAnswerTime}, forwarding Keep Alives", answeredPromptly, @event.Link.Server, grace.PromptAnswerTime);
                else
                    @event.Player.Logger.LogDebug("Player answered a Keep Alive on {Server} after {Waited}, {Count} of {Required} prompt answers in a row", @event.Link.Server, waited, answeredPromptly, grace.PromptAnswers);

                break;
        }
    }

    [Subscribe]
    public void OnLinkStopped(LinkStoppedEvent @event)
    {
        lock (_lock)
        {
            if (_grace is null || !ReferenceEquals(_grace.Link, @event.Link))
                return;
        }

        ReplaceGrace(grace: null);
    }

    public void Dispose()
    {
        ReplaceGrace(grace: null);
        GC.SuppressFinalize(this);
    }

    private async ValueTask AnswerAsync(MessageReceivedEvent @event, SlowJoinGrace grace, KeepAliveRequestPacket keepAliveRequest, CancellationToken cancellationToken)
    {
        @event.Cancel();
        await @event.Link.SendPacketAsync(new KeepAliveResponsePacket { Id = keepAliveRequest.Id }, cancellationToken);

        var player = @event.Player;
        long probeId = KeepAliveTracker.CreateRequestId(player.ProtocolVersion);

        if (player.Phase is not Phase.Play || !grace.TryStartProbe(probeId, DateTimeOffset.UtcNow))
        {
            player.Logger.LogTrace("Answered Keep Alive request {Id} from {Server} while the player loads", keepAliveRequest.Id, @event.Link.Server);
            return;
        }

        player.Logger.LogTrace("Answered Keep Alive request {Id} from {Server} while the player loads, probing with {ProbeId}", keepAliveRequest.Id, @event.Link.Server, probeId);
        await player.SendPacketAsync(new KeepAliveRequestPacket { Id = probeId }, cancellationToken);
        ScheduleTimeout(grace, player);
    }

    private int? GetArgument(Option<int> option)
    {
        return console.TryGetOptionValue(option, out var value) ? value : null;
    }

    private void ScheduleTimeout(SlowJoinGrace grace, IPlayer player)
    {
        var cancellation = new CancellationTokenSource();

        lock (_lock)
        {
            if (!ReferenceEquals(_grace, grace))
            {
                cancellation.Dispose();
                return;
            }

            _timeoutCancellation = cancellation;
            _timeoutTask = TimeOutAsync(grace, player, cancellation.Token);
        }
    }

    private static async Task TimeOutAsync(SlowJoinGrace grace, IPlayer player, CancellationToken cancellationToken)
    {
        await Task.Delay(grace.Timeout, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (cancellationToken.IsCancellationRequested || !ReferenceEquals(player.Link, grace.Link) || !grace.TryTimeOut(DateTimeOffset.UtcNow))
            return;

        player.Logger.LogInformation("Keep alive timed out while loading on {Server}", grace.Link.Server);

        try
        {
            await player.KickAsync("Timed out", CancellationToken.None);
        }
        catch (Exception exception)
        {
            player.Logger.LogError(exception, "Failed to kick player that timed out while loading");
        }
    }

    private void CancelTimeout()
    {
        lock (_lock)
        {
            _timeoutCancellation?.Cancel();
            _timeoutCancellation?.Dispose();
            _timeoutCancellation = null;
        }
    }

    private void ReplaceGrace(SlowJoinGrace? grace)
    {
        lock (_lock)
        {
            _grace?.End();
            _grace = grace;
            _timeoutCancellation?.Cancel();
            _timeoutCancellation?.Dispose();
            _timeoutCancellation = null;
        }
    }
}
