using System.Diagnostics;

using Void.Client.Abstractions;

namespace Void.Client;

/// <summary>Adapts <see cref="Process"/> into the lifecycle surface owned by the coordinator.</summary>
internal sealed class ManagedProcess(Process process, int? memoryMb, long? initialOutOfMemoryKillCount, Task? outputCompletion = null) : IManagedProcess
{
    private bool? _wasOutOfMemoryKilled;

    public int? ExitCode => process.HasExited ? process.ExitCode : null;

    public bool HasExited => process.HasExited;

    public int Id => process.Id;

    public int? MemoryMb { get; } = memoryMb;

    public bool WasOutOfMemoryKilled => process.HasExited && process.ExitCode is 137 && (_wasOutOfMemoryKilled ??= initialOutOfMemoryKillCount is { } initialCount
                                               && CgroupMemoryEvents.ReadOutOfMemoryKillCount() is { } currentCount
                                               && currentCount > initialCount);

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
            await outputCompletion.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }
}
