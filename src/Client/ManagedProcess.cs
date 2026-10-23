using System.Diagnostics;

using Void.Client.Abstractions;

namespace Void.Client;

/// <summary>Adapts <see cref="Process"/> into the lifecycle surface owned by the coordinator.</summary>
internal sealed class ManagedProcess(
    Process process,
    int? memoryMb,
    long? initialOutOfMemoryKillCount,
    Task? outputCompletion = null,
    TimeSpan? outputGracePeriod = null,
    string? minecraftDirectory = null,
    DateTime launchedAt = default,
    GameErrorLog? errors = null
) : IManagedProcess
{
    /// <summary>How long the game's output may stay open after its launcher exited.</summary>
    public static readonly TimeSpan DefaultOutputGracePeriod = TimeSpan.FromSeconds(seconds: 5);
    private bool _crashReportRead;
    private bool? _wasOutOfMemoryKilled;

    /// <summary>Gets what the game's crash report, or its Java runtime's error log, says once it exited, or <see langword="null"/> without one.</summary>
    public string? CrashCause
    {
        get
        {
            if (!process.HasExited || minecraftDirectory is null)
                return null;

            if (!_crashReportRead)
            {
                field = CrashReports.ReadCrashCause(minecraftDirectory, launchedAt);
                _crashReportRead = true;
            }

            return field;
        }
    }

    /// <summary>Gets the launcher's exit code. PortableMC exits 0 whatever the game did, so it doesn't tell how Minecraft ended.</summary>
    public int? ExitCode => process.HasExited ? process.ExitCode : null;

    public bool HasExited => process.HasExited;

    public int Id => process.Id;

    /// <summary>Gets the last error the game logged shortly before it exited, or <see langword="null"/> without one.</summary>
    public string? LastError => process.HasExited ? errors?.ReadLastError(process.ExitTime.ToUniversalTime()) : null;

    public int? MemoryMb { get; } = memoryMb;

    // The kernel kills the game's JVM rather than the launcher, whose exit code stays 0, so only the cgroup's kill counter tells.
    public bool WasOutOfMemoryKilled => process.HasExited && (_wasOutOfMemoryKilled ??= initialOutOfMemoryKillCount is { } initialCount
                                               && CgroupMemory.ReadOutOfMemoryKillCount() is { } currentCount
                                               && currentCount > initialCount);

    /// <summary>
    /// Waits for the game's output to end after its launcher exited, but no longer than <paramref name="gracePeriod"/>. A process that
    /// inherited the game's output, such as a helper a mod started, can keep it open after the game exited, which must not hide the exit.
    /// </summary>
    public static async Task WaitForOutputAsync(Task output, TimeSpan gracePeriod, CancellationToken cancellationToken)
    {
        var outputWait = output.WaitAsync(gracePeriod, cancellationToken);
        await outputWait.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (outputWait.Exception?.GetBaseException() is TimeoutException)
        {
            await Console.Error.WriteLineAsync(
                $"The game's output was still open {gracePeriod.TotalSeconds:0.#} seconds after its launcher exited, because another process holds it; continuing without the rest"
            ).ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        await outputWait.ConfigureAwait(continueOnCapturedContext: false);
    }

    public void Dispose()
    {
        process.Dispose();
    }

    public void KillTree()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (outputCompletion is not null)
            await WaitForOutputAsync(outputCompletion, outputGracePeriod ?? DefaultOutputGracePeriod, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }
}
