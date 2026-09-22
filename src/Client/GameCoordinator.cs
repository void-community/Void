using System.Threading.Channels;

using Void.Client.Abstractions;
using Void.Client.Failures;
using Void.Client.Models;
using Void.Client.Requests;
using Void.Client.States;

namespace Void.Client;

/// <summary>
/// Serializes all lifecycle and X11 mutations through one channel. Status reads are lock-free because only the
/// channel reader publishes immutable snapshots.
/// </summary>
internal sealed partial class GameCoordinator(IGameRuntime runtime, ILogger<GameCoordinator> logger, SessionDiagnostics? diagnostics = null) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogGameStopFailure = LoggerMessage.Define(LogLevel.Error, new EventId(id: 1003, nameof(LogGameStopFailure)), formatString: "Game stop failed");
    private static readonly Action<ILogger, string, Exception?> LogOperationFailure = LoggerMessage.Define<string>(LogLevel.Error, new EventId(id: 1002, nameof(LogOperationFailure)), formatString: "{Operation} failed");
    private static readonly Action<ILogger, string, Exception?> LogRejectedMessage = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(id: 1004, nameof(LogRejectedMessage)),
        formatString: "Coordinator stopped before it could record {MessageType}"
    );
    private static readonly Action<ILogger, StopMode, Exception?> LogShutdownStopResult = LoggerMessage.Define<StopMode>(
        LogLevel.Debug,
        new EventId(id: 1005, nameof(LogShutdownStopResult)),
        formatString: "Stopped Minecraft during API shutdown with mode {StopMode}"
    );
    private static readonly Action<ILogger, StopMode, Exception?> LogSupersededGameStopResult = LoggerMessage.Define<StopMode>(
        LogLevel.Debug,
        new EventId(id: 1006, nameof(LogSupersededGameStopResult)),
        formatString: "Stopped superseded Minecraft game with mode {StopMode}"
    );
    private static readonly Action<ILogger, Exception?> LogShutdownFailure = LoggerMessage.Define(LogLevel.Error, new EventId(id: 1001, nameof(LogShutdownFailure)), formatString: "Failed to stop Minecraft during API shutdown");
    private readonly Channel<Message> _messages = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
    private readonly List<Task> _ownedTasks = [];
    private readonly List<ConnectWaiter> _connectWaiters = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, (Task<RunningGame> Operation, CancellationTokenSource Cancellation, IDisposable? DiagnosticContext)> _startObservations = new();
    private CancellationTokenSource? _activeCancellation;
    private long? _connectOperationId;
    private ConnectGameResponse? _connectedResponse;
    private ServerAddress? _connectingServer;
    private RunningGame? _game;
    private long _nextOperationId;
    private GameProcessExitException? _processExitFailure;
    private Guid? _sessionId;
    private int _started;
    private GameStatus _status = new(
        GameState.Idle,
        OperationId: 0,
        Operation: null,
        OperationState.None,
        ProcessId: null,
        ExitCode: null,
        Server: null,
        Message: null,
        Error: null,
        Failure: null,
        [],
        DateTimeOffset.UtcNow
    );
    private CancellationToken _stoppingToken;

    public bool IsHealthy => Volatile.Read(ref _started) is 1;

    public GameStatus Status => Volatile.Read(ref _status);

    public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        return await EnqueueAsync<byte[]>(completion => new ScreenshotMessage(completion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<ConnectGameResponse> ConnectAsync(ConnectGameRequest request, CancellationToken cancellationToken)
    {
        return await EnqueueAsync<ConnectGameResponse>(completion => new ConnectMessage(request, completion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public override void Dispose()
    {
        _activeCancellation?.Dispose();
        base.Dispose();
    }

    public async Task<GamePlayers> GetPlayersAsync(CancellationToken cancellationToken)
    {
        return await EnqueueAsync<GamePlayers>(completion => new PlayersMessage(completion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task SendChatAsync(SendChatRequest request, CancellationToken cancellationToken)
    {
        await EnqueueAsync(completion => new SendChatMessage(request, completion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<GameStatus> StartCurseForgeAsync(StartCurseForgeGameRequest request, CancellationToken cancellationToken)
    {
        return await EnqueueAsync<GameStatus>(
            completion => new StartMessage(Kind: "start-curseforge", Request: null, NeoForgeRequest: null, request, completion),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<GameStatus> StartNeoForgeAsync(StartNeoForgeGameRequest request, CancellationToken cancellationToken)
    {
        return await EnqueueAsync<GameStatus>(
            completion => new StartMessage(Kind: "start-neoforge", Request: null, request, CurseForgeRequest: null, completion),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<GameStatus> StartVanillaAsync(StartGameRequest request, CancellationToken cancellationToken)
    {
        return await EnqueueAsync<GameStatus>(
            completion => new StartMessage(Kind: "start-vanilla", request, NeoForgeRequest: null, CurseForgeRequest: null, completion),
            cancellationToken
        ).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<StopGameResponse> StopGameAsync(CancellationToken cancellationToken)
    {
        return await EnqueueAsync<StopGameResponse>(static completion => new StopMessage(completion), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task WriteOptionsAsync(string options, CancellationToken cancellationToken)
    {
        await EnqueueAsync(completion => new OptionsMessage(options, completion, cancellationToken), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        Volatile.Write(ref _started, value: 1);

        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false))
                await HandleAsync(message).ConfigureAwait(continueOnCapturedContext: false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            Volatile.Write(ref _started, value: 0);
            _messages.Writer.Complete();

            if (_activeCancellation is not null)
                await _activeCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);

            if (_game is not null)
            {
                var stopTask = runtime.StopAsync(_game, CancellationToken.None);
                await ((Task)stopTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                if (stopTask.IsCompletedSuccessfully)
                {
                    var stopMode = await stopTask.ConfigureAwait(continueOnCapturedContext: false);
                    LogShutdownStopResult(logger, stopMode, arg3: null);
                }
                else
                {
                    LogShutdownFailure(logger, GetTaskException(stopTask));
                }
            }

            await Task.WhenAll(_ownedTasks).ConfigureAwait(continueOnCapturedContext: false);

            if (_sessionId is { } sessionId)
                await (diagnostics?.CompleteAsync(sessionId, CancellationToken.None) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private static GameCommandException BadRequest(string message)
    {
        return new(StatusCodes.Status400BadRequest, message);
    }

    private static GameCommandException Conflict(string message)
    {
        return new(StatusCodes.Status409Conflict, message);
    }

    private static ClientFailure FailureFor(Exception exception, string operation, string stage = "coordinator")
    {
        return ClientFailure.FromException(code: "client.operation.failed", operation, stage, exception);
    }

    private static Exception GetTaskException(Task task)
    {
        return task.Exception?.GetBaseException() ?? new TaskCanceledException(task);
    }

    private static bool IsMaximumHeapArgument(string argument)
    {
        return argument.StartsWith(value: "-Xmx", StringComparison.Ordinal)
               || argument.StartsWith(value: "--jvm-arg=-Xmx", StringComparison.Ordinal);
    }

    private static async Task ObservePlayersAsync(Task<GamePlayers> operation, TaskCompletionSource<GamePlayers> completion)
    {
        await ((Task)operation).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (operation.IsCompletedSuccessfully)
            completion.SetResult(await operation.ConfigureAwait(continueOnCapturedContext: false));
        else
            completion.SetException(GetTaskException(operation));
    }

    private void AddConnectWaiter(ConnectMessage message)
    {
        var registration = message.RequestCancellation.Register(
            () =>
        {
            if (!_messages.Writer.TryWrite(new ConnectWaiterCanceled(message.Completion, message.RequestCancellation)))
                LogRejectedMessage(logger, nameof(ConnectWaiterCanceled), arg3: null);
        }
        );

        _connectWaiters.Add(new(message.Completion, registration));
    }

    private async Task<(long OperationId, CancellationTokenSource Cancellation)> BeginConfirmedOperationAsync(string operation, CancellationToken requestCancellation)
    {
        long operationId = ++_nextOperationId;
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken, requestCancellation);
        _activeCancellation = cancellation;
        await PublishAsync(
            Status with { OperationId = operationId, Operation = operation, OperationState = OperationState.Running, Message = $"{operation} running", Error = null, Failure = null, UpdatedAt = DateTimeOffset.UtcNow }
        ).ConfigureAwait(continueOnCapturedContext: false);

        return (operationId, cancellation);
    }

    private void CancelConnectWaiters()
    {
        foreach (var waiter in _connectWaiters)
        {
            waiter.CancellationRegistration.Dispose();
            waiter.Completion.SetCanceled();
        }

        _connectWaiters.Clear();
        _connectingServer = null;
        _connectOperationId = null;
    }

    private async Task CaptureFailureAsync(long operationId)
    {
        if (diagnostics?.CurrentSessionId is not { } sessionId)
            return;

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(seconds: 3));

        var evidenceTask = CaptureFailureEvidenceAsync(diagnostics, sessionId, operationId, timeout.Token);
        await evidenceTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (!evidenceTask.IsCompletedSuccessfully)
            await diagnostics.WarnAsync(sessionId, $"Failure screenshot unavailable: {GetTaskException(evidenceTask).Message}", _stoppingToken).ConfigureAwait(continueOnCapturedContext: false);

        await diagnostics.CollectAsync(sessionId, _stoppingToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task CaptureFailureEvidenceAsync(SessionDiagnostics sessionDiagnostics, Guid sessionId, long operationId, CancellationToken cancellationToken)
    {
        byte[] screenshot = await runtime.CaptureScreenshotAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await sessionDiagnostics.SaveScreenshotAsync(sessionId, operationId, screenshot, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task CleanupSupersededGameAsync(RunningGame game)
    {
        try
        {
            var stopMode = await runtime.StopAsync(game, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
            LogSupersededGameStopResult(logger, stopMode, arg3: null);
        }
        finally
        {
            game.Dispose();
        }
    }

    private async Task<bool> CompleteConfirmedOperationAsync(long operationId, string operation, Exception? error, bool canceled, Action cancelCompletion, Action<Exception> failCompletion)
    {
        if (operationId != Status.OperationId)
        {
            failCompletion(Conflict($"{operation} was superseded by operation {Status.OperationId}"));

            return false;
        }

        _activeCancellation = null;

        if (error is null)
        {
            await PublishAsync(
                Status with { OperationState = OperationState.Succeeded, Message = $"{operation} succeeded", Error = null, Failure = null, UpdatedAt = DateTimeOffset.UtcNow }
            ).ConfigureAwait(continueOnCapturedContext: false);

            return true;
        }

        var operationState = canceled ? OperationState.Canceled : OperationState.Failed;
        string operationStateDescription = canceled ? "canceled" : "failed";
        await PublishAsync(
            Status with { OperationState = operationState, Message = $"{operation} {operationStateDescription}", Error = canceled ? null : error.Message, Failure = canceled ? null : FailureFor(error, operation), UpdatedAt = DateTimeOffset.UtcNow }
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (canceled)
            cancelCompletion();
        else
            failCompletion(error);

        return false;
    }

    private async Task<TResult> EnqueueAsync<TResult>(Func<TaskCompletionSource<TResult>, Message> createMessage, CancellationToken cancellationToken)
    {
        TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _messages.Writer.WriteAsync(createMessage(completion), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task EnqueueAsync(Func<TaskCompletionSource, Message> createMessage, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _messages.Writer.WriteAsync(createMessage(completion), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private void FailConnectWaiters(Exception exception)
    {
        foreach (var waiter in _connectWaiters)
        {
            waiter.CancellationRegistration.Dispose();
            waiter.Completion.SetException(exception);
        }

        _connectWaiters.Clear();
        _connectingServer = null;
        _connectOperationId = null;
    }

    private async Task HandleAsync(Message message)
    {
        using var diagnosticContext = message is StartMessage ? null : diagnostics?.Enter(_sessionId);

        switch (message)
        {
            case StartMessage start:
                {
                    await HandleStartAsync(start).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case StopMessage stop:
                {
                    await HandleStopAsync(stop).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ConnectMessage connect:
                {
                    await HandleConnectAsync(connect).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ConnectWaiterCanceled canceled:
                {
                    HandleConnectWaiterCanceled(canceled);

                    break;
                }
            case SendChatMessage chat:
                {
                    await HandleSendChatAsync(chat).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ScreenshotMessage screenshot:
                {
                    await HandleScreenshotAsync(screenshot).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case PlayersMessage players:
                {
                    HandlePlayers(players);

                    break;
                }
            case OptionsMessage options:
                {
                    await HandleOptionsAsync(options).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case StartCompleted completed:
                {
                    await HandleStartCompletedAsync(completed).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case StopCompleted completed:
                {
                    await HandleStopCompletedAsync(completed).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ConnectCompleted completed:
                {
                    await HandleConnectCompletedAsync(completed).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case VoidOperationCompleted completed:
                {
                    await HandleVoidOperationCompletedAsync(completed).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ScreenshotCompleted completed:
                {
                    await HandleScreenshotCompletedAsync(completed).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            case ProcessExited exited:
                {
                    await HandleProcessExitedAsync(exited).ConfigureAwait(continueOnCapturedContext: false);

                    break;
                }
            default:
                throw new InvalidOperationException($"Unknown coordinator message type {message.GetType().Name}");
        }
    }

    private async Task HandleConnectAsync(ConnectMessage message)
    {
        string? host = message.Request.Host?.Trim();

        if (string.IsNullOrWhiteSpace(host) || message.Request.Port is < 1 or > 65535)
        {
            message.Completion.SetException(BadRequest(message: "host and a port between 1 and 65535 are required"));

            return;
        }

        ServerAddress server = new(host, message.Request.Port);

        if (_game is null)
        {
            message.Completion.SetException(Conflict(message: "A running game is required before connecting"));

            return;
        }

        if (Status.State is GameState.Connected)
        {
            if (_connectedResponse?.Server == server)
                message.Completion.SetResult(_connectedResponse);
            else
                message.Completion.SetException(Conflict(message: "The game is already connected to a different server"));

            return;
        }

        if (Status.State is not GameState.Ready)
        {
            message.Completion.SetException(Conflict(message: "A ready game is required before connecting"));

            return;
        }

        if (_connectingServer is not null)
        {
            if (_connectingServer == server)
                AddConnectWaiter(message);
            else
                message.Completion.SetException(Conflict(message: "A connection to a different server is already in progress"));

            return;
        }

        if (_activeCancellation is not null)
        {
            message.Completion.SetException(Conflict(message: "Another game operation is running"));

            return;
        }

        long operationId = ++_nextOperationId;
        CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _activeCancellation = cancellation;
        _connectingServer = server;
        _connectOperationId = operationId;
        AddConnectWaiter(message);
        await PublishAsync(
            Status with { OperationId = operationId, Operation = "connect", OperationState = OperationState.Running, Message = "connect running", Error = null, Failure = null, UpdatedAt = DateTimeOffset.UtcNow }
        ).ConfigureAwait(continueOnCapturedContext: false);
        Own(
            ObserveConnectAsync(operationId, server, runtime.ConnectAsync(_game, host, message.Request.Port, cancellation.Token), cancellation)
        );
    }

    private async Task HandleConnectCompletedAsync(ConnectCompleted completed)
    {
        completed.Cancellation.Dispose();

        if (_connectOperationId != completed.OperationId)
            return;

        var waiters = _connectWaiters.ToArray();
        _connectWaiters.Clear();

        foreach (var waiter in waiters)
            await waiter.CancellationRegistration.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);

        _connectingServer = null;
        _connectOperationId = null;

        if (completed.OperationId != Status.OperationId)
        {
            foreach (var waiter in waiters)
                waiter.Completion.SetException(Conflict($"connect was superseded by operation {Status.OperationId}"));

            return;
        }

        _activeCancellation = null;

        if (completed.Error is not null)
        {
            var operationState = completed.Canceled ? OperationState.Canceled : OperationState.Failed;
            string operationStateDescription = completed.Canceled ? "canceled" : "failed";
            await PublishAsync(
                Status with { OperationState = operationState, Message = $"connect {operationStateDescription}", Error = completed.Canceled ? null : completed.Error.Message, Failure = completed.Canceled ? null : FailureFor(completed.Error, operation: "connect"), UpdatedAt = DateTimeOffset.UtcNow }
            ).ConfigureAwait(continueOnCapturedContext: false);

            foreach (var waiter in waiters)
            {
                if (completed.Canceled)
                    waiter.Completion.SetCanceled();
                else
                    waiter.Completion.SetException(completed.Error);
            }

            return;
        }

        _connectedResponse = new(completed.Server, DateTimeOffset.UtcNow);
        await PublishAsync(
            Status with { State = GameState.Connected, Server = completed.Server, OperationState = OperationState.Succeeded, Message = "Interactive game connection confirmed", Error = null, Failure = null, UpdatedAt = DateTimeOffset.UtcNow }
        ).ConfigureAwait(continueOnCapturedContext: false);

        foreach (var waiter in waiters)
            waiter.Completion.SetResult(_connectedResponse);
    }

    private void HandleConnectWaiterCanceled(ConnectWaiterCanceled message)
    {
        int waiterIndex = _connectWaiters.FindIndex(waiter => waiter.Completion == message.Completion);

        if (waiterIndex < 0)
            return;

        var waiter = _connectWaiters[waiterIndex];
        waiter.CancellationRegistration.Dispose();
        _connectWaiters.RemoveAt(waiterIndex);
        waiter.Completion.SetCanceled(message.CancellationToken);

        // The accepted connection intent outlives individual HTTP waiters. Stop and process-exit paths still own
        // cancellation of the background operation.
    }

    private async Task HandleOptionsAsync(OptionsMessage message)
    {
        if (_activeCancellation is not null)
        {
            message.Completion.SetException(Conflict(message: "Options cannot change while another game operation is running"));

            return;
        }

        var (operationId, cancellation) = await BeginConfirmedOperationAsync(operation: "options", message.RequestCancellation).ConfigureAwait(continueOnCapturedContext: false);
        Own(
            ObserveVoidOperationAsync(operationId, kind: "options", runtime.WriteOptionsAsync(message.Options, cancellation.Token), cancellation, message.Completion)
        );
    }

    private void HandlePlayers(PlayersMessage message)
    {
        if (_game is null || Status.State is not (GameState.Ready or GameState.Connected))
        {
            message.Completion.SetException(Conflict(message: "A running game is required before reading its players"));

            return;
        }

        Own(ObservePlayersAsync(runtime.ReadPlayersAsync(_game, message.RequestCancellation), message.Completion));
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

        var processFailure = exited.ExitCode is not 0 || processExitedDuringOperation
            ? new GameProcessExitException(exited.ExitCode, exited.WasOutOfMemoryKilled, exited.MemoryMb)
            : null;

        _game.Dispose();
        _game = null;
        _connectedResponse = null;
        Volatile.Write(ref _processExitFailure, processFailure);

        if (_activeCancellation is not null)
            await _activeCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);

        if (processFailure is null)
            CancelConnectWaiters();
        else
            FailConnectWaiters(processFailure);

        _activeCancellation = null;
        await PublishAsync(
            Status with
            {
                State = processFailure is null ? GameState.Idle : GameState.Failed,
                OperationState = processFailure is null ? OperationState.Succeeded : OperationState.Failed,
                ProcessId = null,
                ExitCode = exited.ExitCode,
                Server = null,
                Message = processFailure is null ? "Game exited" : "Game exited unexpectedly",
                Error = processFailure?.Message,
                Failure = processFailure?.Failure,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (_sessionId is { } sessionId)
            await (diagnostics?.CompleteAsync(sessionId, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task HandleScreenshotAsync(ScreenshotMessage message)
    {
        if (_game is null || Status.State is not (GameState.Ready or GameState.Connected))
        {
            message.Completion.SetException(Conflict(message: "A running game is required before taking a screenshot"));

            return;
        }

        if (_activeCancellation is not null)
        {
            message.Completion.SetException(Conflict(message: "Another game operation is running"));

            return;
        }

        var (operationId, cancellation) = await BeginConfirmedOperationAsync(operation: "screenshot", message.RequestCancellation).ConfigureAwait(continueOnCapturedContext: false);
        Own(ObserveScreenshotAsync(operationId, runtime.CaptureScreenshotAsync(cancellation.Token), cancellation, message.Completion));
    }

    private async Task HandleScreenshotCompletedAsync(ScreenshotCompleted completed)
    {
        completed.Cancellation.Dispose();

        bool operationCompleted = await CompleteConfirmedOperationAsync(
            completed.OperationId,
            operation: "screenshot",
            completed.Error,
            completed.Canceled,
            completed.Completion.SetCanceled,
            completed.Completion.SetException
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (!operationCompleted)
            return;

        completed.Completion.SetResult(completed.Image ?? throw new InvalidOperationException(message: "Screen capture returned no image"));
    }

    private async Task HandleSendChatAsync(SendChatMessage message)
    {
        string? text = message.Request.Message;

        if (_game is null || Status.State is not GameState.Connected)
        {
            message.Completion.SetException(Conflict(message: "A connected game is required before sending chat"));

            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            message.Completion.SetException(BadRequest(message: "message is required"));

            return;
        }

        if (_activeCancellation is not null)
        {
            message.Completion.SetException(Conflict(message: "Another game operation is running"));

            return;
        }

        var (operationId, cancellation) = await BeginConfirmedOperationAsync(operation: "send-chat", message.RequestCancellation).ConfigureAwait(continueOnCapturedContext: false);
        Own(
            ObserveVoidOperationAsync(operationId, kind: "send-chat", runtime.SendChatAsync(_game, text, cancellation.Token), cancellation, message.Completion)
        );
    }

    private async Task HandleStartAsync(StartMessage message)
    {
        if (_activeCancellation is not null || _game is not null || Status.State is not (GameState.Idle or GameState.Failed))
        {
            message.Completion.SetException(Conflict(message: "A game is already running or changing state"));

            return;
        }

        string? version = message.Request?.Version?.Trim();
        string? neoForgeVersion = message.NeoForgeRequest?.Version?.Trim();
        string? slug = message.CurseForgeRequest?.Slug?.Trim();
        string[] arguments = message.Request?.Arguments ?? message.NeoForgeRequest?.Arguments ?? message.CurseForgeRequest?.Arguments ?? [];
        int? memoryMb = message.Request?.MemoryMb ?? message.NeoForgeRequest?.MemoryMb ?? message.CurseForgeRequest?.MemoryMb;

        if (message.Kind is "start-vanilla" && string.IsNullOrWhiteSpace(version))
        {
            message.Completion.SetException(BadRequest(message: "version is required"));

            return;
        }

        if (message.Kind is "start-curseforge" && (string.IsNullOrWhiteSpace(slug) || message.CurseForgeRequest?.FileId <= 0))
        {
            message.Completion.SetException(BadRequest(message: "slug and a positive fileId are required"));

            return;
        }

        if (memoryMb is <= 0)
        {
            message.Completion.SetException(BadRequest(message: "memoryMb must be a positive integer"));

            return;
        }

        if (memoryMb is not null && arguments.Any(IsMaximumHeapArgument))
        {
            message.Completion.SetException(BadRequest(message: "memoryMb cannot be combined with an -Xmx JVM argument"));

            return;
        }

        if (_sessionId is { } previousSession)
            await (diagnostics?.CompleteAsync(previousSession, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);

        _sessionId = diagnostics is null ? null : await diagnostics.BeginAsync(
            $"{message.Kind}:{version ?? neoForgeVersion ?? slug}:{message.CurseForgeRequest?.FileId}",
            Environment.GetEnvironmentVariable(variable: "MINECRAFT_DIRECTORY") ?? "/root/.minecraft",
            _stoppingToken
        ).ConfigureAwait(continueOnCapturedContext: false);

        using var diagnosticContext = diagnostics?.Enter(_sessionId);

        long operationId = ++_nextOperationId;
        CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _activeCancellation = operationCancellation;
        _connectedResponse = null;
        _processExitFailure = null;
        await PublishAsync(
            new(
                GameState.Starting,
                operationId,
                message.Kind,
                OperationState.Running,
                ProcessId: null,
                ExitCode: null,
                Server: null,
                Message: "Game launch accepted",
                Error: null,
                Failure: null,
                [],
                DateTimeOffset.UtcNow
            )
        ).ConfigureAwait(continueOnCapturedContext: false);

        IDisposable? operationDiagnosticContext = diagnostics?.Enter(_sessionId);

        try
        {
            Task<RunningGame> operation = message.Kind switch
            {
                "start-vanilla" => runtime.LaunchVanillaAsync(version ?? "", arguments, memoryMb, operationCancellation.Token),
                "start-neoforge" => runtime.LaunchNeoForgeAsync(neoForgeVersion ?? "", arguments, memoryMb, operationCancellation.Token),
                "start-curseforge" => runtime.LaunchCurseForgeAsync(slug ?? "", message.CurseForgeRequest?.FileId ?? 0, arguments, memoryMb, operationCancellation.Token),
                _ => throw new InvalidOperationException($"Unknown launch kind {message.Kind}")
            };

            if (!_startObservations.TryAdd(operationId, (operation, operationCancellation, operationDiagnosticContext)))
                throw new InvalidOperationException($"Start observation {operationId} is already registered");

            operationDiagnosticContext = null;
            Own(ObserveStartAsync(operationId, message.Kind));
        }
        finally
        {
            operationDiagnosticContext?.Dispose();
        }

        message.Completion.SetResult(Status);
    }

    private async Task HandleStartCompletedAsync(StartCompleted completed)
    {
        completed.Cancellation.Dispose();

        if (completed.OperationId != Status.OperationId || Status.Operation is "stop")
        {
            if (completed.Game is not null)
                Own(CleanupSupersededGameAsync(completed.Game));

            return;
        }

        _activeCancellation = null;

        if (completed.Error is not null)
        {
            LogOperationFailure(logger, completed.Kind, completed.Error);
            await PublishAsync(
                Status with
                {
                    State = completed.Canceled ? GameState.Idle : GameState.Failed,
                    OperationState = completed.Canceled ? OperationState.Canceled : OperationState.Failed,
                    Message = completed.Canceled ? "Game launch canceled" : "Game launch failed",
                    Error = completed.Canceled ? null : completed.Error.Message,
                    Failure = completed.Canceled ? null : FailureFor(completed.Error, completed.Kind),
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ).ConfigureAwait(continueOnCapturedContext: false);

            if (_sessionId is { } sessionId)
                await (diagnostics?.CompleteAsync(sessionId, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        _game = completed.Game ?? throw new InvalidOperationException(message: "A successful launch did not return a game process");
        await PublishAsync(
            Status with
            {
                State = GameState.Ready,
                OperationState = OperationState.Succeeded,
                ProcessId = _game.Process.Id,
                ExitCode = null,
                Message = "Game window is ready",
                Error = null,
                Failure = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);
        Own(MonitorProcessAsync(_game));
    }

    private async Task HandleStopAsync(StopMessage message)
    {
        if (Status.State is GameState.Stopping)
        {
            message.Completion.SetException(Conflict(message: "The game is already stopping"));

            return;
        }

        if (_activeCancellation is not null)
            await _activeCancellation.CancelAsync().ConfigureAwait(continueOnCapturedContext: false);

        CancelConnectWaiters();
        long operationId = ++_nextOperationId;
        CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _activeCancellation = operationCancellation;
        await PublishAsync(
            Status with
            {
                State = GameState.Stopping,
                OperationId = operationId,
                Operation = "stop",
                OperationState = OperationState.Running,
                Message = "Stopping game",
                Error = null,
                Failure = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        ).ConfigureAwait(continueOnCapturedContext: false);
        Own(ObserveStopAsync(operationId, runtime.StopAsync(_game, operationCancellation.Token), operationCancellation, message.Completion));
    }

    private async Task HandleStopCompletedAsync(StopCompleted completed)
    {
        completed.Cancellation.Dispose();

        if (completed.OperationId != Status.OperationId)
            return;

        _activeCancellation = null;

        if (completed.Error is not null)
        {
            LogGameStopFailure(logger, completed.Error);
            await PublishAsync(
                Status with { State = GameState.Failed, OperationState = OperationState.Failed, Error = completed.Error.Message, Failure = FailureFor(completed.Error, operation: "stop"), Message = "Game stop failed", UpdatedAt = DateTimeOffset.UtcNow }
            ).ConfigureAwait(continueOnCapturedContext: false);
            completed.Completion.SetException(completed.Error);

            return;
        }

        int? exitCode = _game?.Process.ExitCode;
        _game?.Dispose();
        _game = null;
        _connectedResponse = null;
        await PublishAsync(
            new(
                GameState.Idle,
                completed.OperationId,
                Operation: "stop",
                OperationState.Succeeded,
                ProcessId: null,
                exitCode,
                Server: null,
                Message: "Game stopped",
                Error: null,
                Failure: null,
                [],
                DateTimeOffset.UtcNow
            )
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (_sessionId is { } sessionId)
            await (diagnostics?.CompleteAsync(sessionId, _stoppingToken) ?? Task.CompletedTask).ConfigureAwait(continueOnCapturedContext: false);

        completed.Completion.SetResult(new(completed.Mode, Status));
    }

    private async Task HandleVoidOperationCompletedAsync(VoidOperationCompleted completed)
    {
        completed.Cancellation.Dispose();

        bool operationCompleted = await CompleteConfirmedOperationAsync(
            completed.OperationId,
            completed.Kind,
            completed.Error,
            completed.Canceled,
            completed.Completion.SetCanceled,
            completed.Completion.SetException
        ).ConfigureAwait(continueOnCapturedContext: false);

        if (!operationCompleted)
            return;

        completed.Completion.SetResult();
    }
}
