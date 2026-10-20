using Void.Client.Models;
using Void.Client.States;

namespace Void.Client.Abstractions;

internal interface IGameRuntime
{
    Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken);

    Task ConnectAsync(RunningGame game, string host, int port, CancellationToken cancellationToken);

    Task<RunningGame> LaunchCurseForgeAsync(string slug, int fileId, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken);

    Task<RunningGame> LaunchNeoForgeAsync(
        string version,
        string? maximumMinecraftVersion,
        IReadOnlyList<string> arguments,
        int? memoryMb,
        CancellationToken cancellationToken
    );

    Task<RunningGame> LaunchVanillaAsync(string version, IReadOnlyList<string> arguments, int? memoryMb, CancellationToken cancellationToken);

    Task<string?> ReadDisconnectReasonAsync(RunningGame game, CancellationToken cancellationToken);

    /// <summary>Reads the memory limit of the container the game runs in.</summary>
    /// <returns>The limit in MiB, or <see langword="null"/> when the container has none or it can't be read.</returns>
    long? ReadMemoryLimitMb();

    /// <summary>Reads whether the game has finished loading and shown a menu.</summary>
    /// <returns>The menu, or <see langword="null"/> while the game is still loading.</returns>
    Task<GameMenu?> ReadMenuAsync(RunningGame game, CancellationToken cancellationToken);

    Task<GamePlayers> ReadPlayersAsync(RunningGame game, CancellationToken cancellationToken);

    Task SendChatAsync(RunningGame game, string message, CancellationToken cancellationToken);

    Task<StopMode> StopAsync(RunningGame? game, CancellationToken cancellationToken);

    Task WriteOptionsAsync(string options, CancellationToken cancellationToken);
}
