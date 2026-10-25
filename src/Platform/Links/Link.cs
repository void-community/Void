using System.Collections.Concurrent;
using Nito.AsyncEx;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Extensions;
using Void.Proxy.Api.Links;
using Void.Proxy.Api.Links.Exceptions;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Network.Messages;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Extensions;
using Void.Proxy.Api.Servers;

namespace Void.Proxy.Links;

public class Link(IPlayer player, IServer server, INetworkChannel playerChannel, INetworkChannel serverChannel, ILogger logger, IEventService events, CancellationToken cancellationToken) : ILink
{
    private readonly CancellationTokenSource _ctsPlayerToServer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly CancellationTokenSource _ctsPlayerToServerForce = new();
    private readonly CancellationTokenSource _ctsServerToPlayer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly CancellationTokenSource _ctsServerToPlayerForce = new();
    private readonly AsyncLock _lock = new();
    private readonly ConcurrentDictionary<Direction, State> _states = [];
    private Task<Task>? _onStoppingTask;

    private Task? _playerToServerTask;
    private bool _playerToServerTaskDisposed;
    private Task? _serverToPlayerTask;
    private bool _serverToPlayerTaskDisposed;
    private bool _stopping;
    private LinkStopReason? _stopReason;

    public IPlayer Player { get; init; } = player;
    public IServer Server { get; init; } = server;
    public INetworkChannel PlayerChannel { get; init; } = playerChannel;
    public INetworkChannel ServerChannel { get; init; } = serverChannel;

    public bool IsAlive => this is { _playerToServerTask.IsCompleted: false, _serverToPlayerTask.IsCompleted: false };

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this is { _playerToServerTask: not null } or { _serverToPlayerTask: not null })
            throw new InvalidOperationException($"{nameof(Link)} was already started");

        await events.ThrowAsync(new LinkStartingEvent(this, Player), cancellationToken);

        _playerToServerTask = ExecuteAsync(PlayerChannel, ServerChannel, Direction.Serverbound, _ctsPlayerToServer.Token, _ctsPlayerToServerForce.Token);
        _serverToPlayerTask = ExecuteAsync(ServerChannel, PlayerChannel, Direction.Clientbound, _ctsServerToPlayer.Token, _ctsServerToPlayerForce.Token);

        _onStoppingTask = Task.WhenAll(_playerToServerTask, _serverToPlayerTask).ContinueWith(task => OnStoppedAsync(task, cancellationToken).CatchExceptions(logger, $"{nameof(LinkStoppedEvent)} caused exception(s)"));

        logger.LogTrace("Started forwarding {Link} traffic", this);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_onStoppingTask is null)
            throw new InvalidOperationException($"{nameof(Link)} is not started");

        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientboundAsync();
        await DisposeServerboundAsync();

        GC.SuppressFinalize(this);
    }

    ~Link()
    {
        _onStoppingTask?.GetAwaiter().GetResult();
    }

    protected async ValueTask DisposeServerboundAsync()
    {
        if (_playerToServerTaskDisposed)
            return;

        if (_playerToServerTask is not null)
        {
            await _ctsPlayerToServer.CancelAsync();

            if (await WaitWithTimeout(_playerToServerTask))
            {
                logger.LogWarning("Timed out waiting for Player {Player} disconnection from Server {Server} manually, cancelling forcefully [States: {States}]", Player, Server, string.Join(", ", _states.Select(x => $"{x.Key}={x.Value}")));
                await _ctsPlayerToServerForce.CancelAsync();

                if (await WaitWithTimeout(_playerToServerTask))
                    throw new Exception($"Cannot dispose Link {this} (server=>player)");
            }

            if (_playerToServerTask.IsCanceled)
                return;

            await _playerToServerTask;
        }
    }

    protected async ValueTask DisposeClientboundAsync()
    {
        if (_serverToPlayerTaskDisposed)
            return;

        if (_serverToPlayerTask is not null)
        {
            await _ctsServerToPlayer.CancelAsync();

            if (await WaitWithTimeout(_serverToPlayerTask))
            {
                logger.LogWarning("Timed out waiting for Server {Server} disconnection from Player {Player} manually, cancelling forcefully [States: {States}]", Server, Player, string.Join(", ", _states.Select(x => $"{x.Key}={x.Value}")));
                await _ctsServerToPlayerForce.CancelAsync();

                if (await WaitWithTimeout(_serverToPlayerTask))
                    throw new Exception($"Cannot dispose Link {this} (player=>server)");
            }

            if (_serverToPlayerTask.IsCanceled)
                return;

            await _serverToPlayerTask;
        }
    }

    protected async Task ExecuteAsync(INetworkChannel sourceChannel, INetworkChannel destinationChannel, Direction direction, CancellationToken cancellationToken, CancellationToken forceCancellationToken)
    {
        await Task.Yield();

        // A server that closes its connection may have sent something last, such as why it kicks the player, which the other direction reads
        var serverClosed = false;

        while (sourceChannel is { CanRead: true } && destinationChannel is { CanWrite: true })
        {
            if (cancellationToken.IsCancellationRequested || forceCancellationToken.IsCancellationRequested)
                break;

            INetworkMessage message;

            var sourceSide = direction is Direction.Serverbound ? Side.Client : Side.Server;
            var destinationSide = direction is Direction.Serverbound ? Side.Server : Side.Client;

            try
            {
                _states[direction] = State.ReadStart;
                message = await sourceChannel.ReadMessageAsync(cancellationToken, forceCancellationToken);
                _states[direction] = State.ReadEnd;

                _states[direction] = State.MessageReceivedEventStart;
                var cancelled = await events.ThrowWithResultAsync(new MessageReceivedEvent(sourceSide, sourceSide, Side.Proxy, direction, message, this, Player), cancellationToken);
                _states[direction] = State.MessageReceivedEventEnd;

                if (cancelled)
                    continue;
            }
            catch (Exception exception) when (exception is TaskCanceledException or OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (exception is StreamException or ObjectDisposedException)
            {
                _stopReason = direction switch
                {
                    Direction.Serverbound => LinkStopReason.PlayerDisconnected, // source (player) disconnected
                    Direction.Clientbound => LinkStopReason.ServerDisconnected, // source (server) disconnected
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
                };

                break;
            }
            catch (Exception exception)
            {
                _stopReason = LinkStopReason.InternalException;
                logger.LogError(exception, "Unhandled read {Direction} exception in {Link} link", direction, this);
                await Player.KickAsync("Unexpected error occurred in your connection.\n(TODO: do not kick)", forceCancellationToken);
                break;
            }

            try
            {
                _states[direction] = State.WriteStart;
                await destinationChannel.WriteMessageAsync(message, forceCancellationToken);
                _states[direction] = State.WriteEnd;

                _states[direction] = State.MessageSentEventStart;
                await events.ThrowAsync(new MessageSentEvent(sourceSide, Side.Proxy, destinationSide, direction, message, this, Player), cancellationToken);
                _states[direction] = State.MessageSentEventEnd;
            }
            catch (Exception exception) when (exception is TaskCanceledException or OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (exception is StreamException or ObjectDisposedException)
            {
                _stopReason = direction switch
                {
                    Direction.Serverbound => LinkStopReason.ServerDisconnected, // destination (server) disconnected
                    Direction.Clientbound => LinkStopReason.PlayerDisconnected, // destination (player) disconnected
                    _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
                };

                serverClosed = direction is Direction.Serverbound;
                break;
            }
            catch (Exception exception)
            {
                _stopReason = LinkStopReason.InternalException;
                logger.LogError(exception, "Unhandled write {Direction} exception in {Link} link", direction, this);
                await Player.KickAsync("Unexpected error occurred in your connection.\n(TODO: do not kick)", forceCancellationToken);
                break;
            }
            finally
            {
                message.Dispose();
            }
        }

        _stopReason ??= LinkStopReason.Requested;

        logger.LogTrace("Stopping {Link} link {Direction} direction with reason {Reason}", direction, this, _stopReason);

        // cancellationToken here is most likely canceled already
        // use forceCancellationToken for events

        try
        {
            using (var _ = await _lock.LockAsync(forceCancellationToken))
            {
                if (!_stopping)
                {
                    _states[direction] = State.Stopping;
                    _stopping = true;
                    await events.ThrowAsync(new LinkStoppingEvent(this, Player, _stopReason ?? throw new LinkInternalException("Stopping reason is unset")), forceCancellationToken);
                }
            }

            if (destinationChannel.CanWrite)
                await destinationChannel.FlushAsync(forceCancellationToken);

            _states[direction] = State.Disposing;

            switch (direction)
            {
                case Direction.Clientbound:
                    _serverToPlayerTaskDisposed = true;
                    await DisposeServerboundAsync();
                    break;
                case Direction.Serverbound:
                    _playerToServerTaskDisposed = true;

                    // The other direction reads what the server sent before it closed, until the connection ends
                    if (serverClosed && _serverToPlayerTask is not null)
                        await WaitWithTimeout(_serverToPlayerTask);

                    await DisposeClientboundAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Link {Link} cannot be stopped", this);
        }
        finally
        {
            _states[direction] = State.Completed;
        }
    }

    public override string ToString()
    {
        return $"{Player} <=> {Server}";
    }

    private static async ValueTask<bool> WaitWithTimeout(Task task, int milliseconds = 5000)
    {
        var timeout = Task.Delay(milliseconds);
        return await Task.WhenAny(timeout, task) == timeout;
    }

    private async Task OnStoppedAsync(Task task, CancellationToken cancellationToken)
    {
        await task;

        // do not wait for completion as this may start initiating a new ILink instance
        await events.ThrowAsync(new LinkStoppedEvent(this, Player, _stopReason ?? throw new LinkInternalException("Stopped reason is unset")), cancellationToken);
    }

    private enum State
    {
        ReadStart,
        ReadEnd,
        MessageReceivedEventStart,
        MessageReceivedEventEnd,
        WriteStart,
        WriteEnd,
        MessageSentEventStart,
        MessageSentEventEnd,
        Stopping,
        Disposing,
        Completed
    }
}
