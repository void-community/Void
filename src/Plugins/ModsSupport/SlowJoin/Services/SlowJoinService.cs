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
/// Answers a backend's keep-alives for a player that is still loading after joining it, until the player answers a keep-alive of
/// the proxy's own. From then on the proxy forwards keep-alives between the backend and the player.
/// </summary>
public class SlowJoinService(Plugin plugin, Settings settings, IConsoleService console) : IEventListener, IDisposable
{
    private readonly Lock _lock = new();
    private SlowJoinGrace? _grace;
    private CancellationTokenSource? _timeoutCancellation;
    private Task? _timeoutTask;

    /// <summary>
    /// Chooses how long a player may take to answer the first keep-alive: the program argument when given, otherwise the setting.
    /// </summary>
    public static TimeSpan GetTimeout(int? argumentSeconds, int settingSeconds)
    {
        return TimeSpan.FromSeconds(Math.Max(argumentSeconds ?? settingSeconds, 1));
    }

    [Subscribe]
    public void OnPlayerJoinedServer(PlayerJoinedServerEvent @event)
    {
        if (!settings.Enabled || !@event.Player.IsMinecraft)
            return;

        int? argumentSeconds = console.TryGetOptionValue(plugin.SlowJoinTimeoutOption, out var seconds) ? seconds : null;
        ReplaceGrace(new SlowJoinGrace(@event.Link, GetTimeout(argumentSeconds, settings.FirstKeepAliveTimeout)));
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
            case KeepAliveResponsePacket keepAliveResponse when grace.TryEnd(keepAliveResponse.Id, DateTimeOffset.UtcNow, out var waited):
                @event.Cancel();
                CancelTimeout();
                @event.Player.Logger.LogInformation("Player answered its first Keep Alive on {Server} after {Waited}, forwarding Keep Alives", @event.Link.Server, waited);
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
