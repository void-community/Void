namespace Void.Client;

/// <summary>Defines the complete external HTTP contract for the reusable game container.</summary>
internal static class ClientApiEndpoints
{
    private const string StatusPath = "/api/game/status";
    private static readonly Action<ILogger, string, Exception?> LogClientOperationFailure = LoggerMessage.Define<string>(LogLevel.Error, new EventId(id: 1000, nameof(LogClientOperationFailure)), formatString: "Client API {Operation} failed");

    public static IEndpointRouteBuilder MapClientApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(prefix: "/api");

        var healthEndpoint = api.MapGet(
            pattern: "/health",
            (GameCoordinator coordinator) => coordinator.IsHealthy
                ? Results.Text(content: "ok")
                : Results.Problem(detail: "Game coordinator is not running", statusCode: StatusCodes.Status503ServiceUnavailable)
        )
            .WithName(endpointName: "ClientHealth")
            .WithSummary(summary: "Reports whether the client API coordinator is ready.");

        var statusEndpoint = api.MapGet(pattern: "/game/status", (GameCoordinator coordinator) => Results.Ok(coordinator.Status))
            .WithName(endpointName: "GetGameStatus")
            .WithSummary(summary: "Returns the current game lifecycle and latest operation status.");

        var diagnosticsListEndpoint = api.MapGet(
            pattern: "/game/diagnostics",
            async (SessionDiagnostics diagnostics, CancellationToken cancellationToken) => Results.Ok(await diagnostics.ListAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
        )
            .WithName(endpointName: "ListGameDiagnostics")
            .WithSummary(summary: "Lists retained Minecraft sessions and diagnostic download URLs.");

        var diagnosticsDownloadEndpoint = api.MapGet(
            pattern: "/game/diagnostics/{sessionId:guid}",
            async Task<IResult> (Guid sessionId, SessionDiagnostics diagnostics, CancellationToken cancellationToken) =>
        {
            var archive = await diagnostics.DownloadAsync(sessionId, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return archive is null ? Results.NotFound() : Results.File(archive, contentType: "application/zip", $"client-diagnostics-{sessionId}.zip");
        }
        )
            .WithName(endpointName: "DownloadGameDiagnostics")
            .WithSummary(summary: "Downloads retained evidence for a running or stopped Minecraft session.");

        var playersEndpoint = api.MapGet(
            pattern: "/game/players",
            async Task<IResult> (GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "players",
                async () => Results.Ok(await coordinator.GetPlayersAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "GetGamePlayers")
            .WithSummary(summary: "Returns the live local player and all other players tracked in the current client world.");

        var optionsEndpoint = api.MapPut(
            pattern: "/game/options",
            async Task<IResult> (HttpRequest request, GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "options",
                async () =>
            {
                using var reader = new StreamReader(request.Body);

                var options = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                await coordinator.WriteOptionsAsync(options, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                return Results.NoContent();
            },
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .Accepts<string>(contentType: "text/plain")
            .WithName(endpointName: "SetGameOptions")
            .WithSummary(summary: "Atomically stores Minecraft options for current and future launches.");

        var vanillaStartEndpoint = api.MapPost(
            pattern: "/game/start/vanilla",
            async Task<IResult> (StartGameRequest request, GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "start-vanilla",
                async () => Results.Accepted(StatusPath, await coordinator.StartVanillaAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "StartVanillaGame")
            .WithSummary(summary: "Starts a Mojang vanilla Minecraft version.");

        var neoForgeStartEndpoint = api.MapPost(
            pattern: "/game/start/neoforge",
            async Task<IResult> (StartNeoForgeGameRequest request, GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "start-neoforge",
                async () => Results.Accepted(StatusPath, await coordinator.StartNeoForgeAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "StartNeoForgeGame")
            .WithSummary(summary: "Starts a NeoForge Minecraft version, or the latest stable release when no version is given.");

        var curseForgeStartEndpoint = api.MapPost(
            pattern: "/game/start/curseforge",
            async Task<IResult> (
                StartCurseForgeGameRequest request,
                GameCoordinator coordinator,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken
            ) =>
            await ExecuteAsync(
                operation: "start-curseforge",
                async () => Results.Accepted(StatusPath, await coordinator.StartCurseForgeAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "StartCurseForgeGame")
            .WithSummary(summary: "Starts a CurseForge modpack file.");

        var stopEndpoint = api.MapPost(
            pattern: "/game/stop",
            async Task<IResult> (GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "stop",
                async () => Results.Ok(await coordinator.StopGameAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "StopGame")
            .WithSummary(summary: "Stops Minecraft and confirms that its process tree exited.");

        var connectEndpoint = api.MapPost(
            pattern: "/game/connect",
            async Task<IResult> (ConnectGameRequest request, GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "connect",
                async () => Results.Ok(await coordinator.ConnectAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "ConnectGame")
            .WithSummary(summary: "Connects to a server and visually confirms an interactive game screen.");

        var chatEndpoint = api.MapPost(
            pattern: "/game/send-chat",
            async Task<IResult> (SendChatRequest request, GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "send-chat",
                async () =>
            {
                await coordinator.SendChatAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                return Results.NoContent();
            },
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "SendGameChat")
            .WithSummary(summary: "Sends and confirms chat input in the connected game.");

        var screenshotEndpoint = api.MapGet(
            pattern: "/game/screenshot",
            async Task<IResult> (GameCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
            await ExecuteAsync(
                operation: "screenshot",
                async () => Results.File(
                    await coordinator.CaptureScreenshotAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false),
                    contentType: "image/png"
                ),
                loggerFactory,
                cancellationToken
            ).ConfigureAwait(continueOnCapturedContext: false)
        )
            .WithName(endpointName: "CaptureGameScreenshot")
            .WithSummary(summary: "Captures the current Minecraft window as PNG.");

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(string operation, Func<Task<IResult>> action, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var actionTask = action();
        await ((Task)actionTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (actionTask.IsCompletedSuccessfully)
            return await actionTask.ConfigureAwait(continueOnCapturedContext: false);

        var exception = actionTask.Exception?.GetBaseException() ?? new TaskCanceledException(actionTask);

        return exception switch
        {
            GameCommandException commandException => Results.Problem(commandException.Message, statusCode: commandException.StatusCode),
            GamePlayersException playersException => Problem(operation, playersException, playersException.StatusCode, playersException.Failure, loggerFactory),
            GameClientException clientException => Problem(operation, clientException, StatusCodes.Status500InternalServerError, clientException.Failure, loggerFactory),
            TimeoutException timeoutException => Problem(
                operation,
                timeoutException,
                StatusCodes.Status504GatewayTimeout,
                ClientFailure.FromException(code: "client.operation.timeout", operation, stage: "timeout", timeoutException),
                loggerFactory
            ),
            OperationCanceledException when cancellationToken.IsCancellationRequested => Results.Problem(detail: "The request was canceled", statusCode: StatusCodes.Status408RequestTimeout),
            _ => Problem(
                operation,
                exception,
                StatusCodes.Status500InternalServerError,
                ClientFailure.FromException(code: "client.operation.failed", operation, stage: "api", exception),
                loggerFactory
            )
        };
    }

    private static IResult Problem(string operation, Exception exception, int statusCode, ClientFailure? failure, ILoggerFactory loggerFactory)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
            LogClientOperationFailure(loggerFactory.CreateLogger(categoryName: "ClientApi"), operation, exception);

        var problem = new ClientProblemDetails(Type: "about:blank", $"Client {operation} failed", statusCode, exception.Message, failure);

        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
