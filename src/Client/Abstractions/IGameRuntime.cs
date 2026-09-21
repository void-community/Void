namespace Void.Client;

internal interface IGameRuntime
{
    Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken);

    Task ConnectAsync(RunningGame game, string host, int port, CancellationToken cancellationToken);

    Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken);

    Task<RunningGame> LaunchNeoForgeAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken);

    Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken);

    Task<GamePlayers> ReadPlayersAsync(RunningGame game, CancellationToken cancellationToken);

    Task SendChatAsync(RunningGame game, string message, CancellationToken cancellationToken);

    Task<StopMode> StopAsync(RunningGame? game, CancellationToken cancellationToken);

    Task WriteOptionsAsync(string options, CancellationToken cancellationToken);
}
