using System.Diagnostics;

namespace Void.Client;

internal sealed record RunningGame : IDisposable
{
    public RunningGame(IManagedProcess process, string version, DateTimeOffset startedAt, GameTrackerConnection tracker)
    {
        Process = process;
        Version = version;
        StartedAt = startedAt;
        Tracker = tracker;
    }

    public RunningGame(
        Process nativeProcess,
        int? memoryMb,
        long? initialOutOfMemoryKillCount,
        Task outputTask,
        string version,
        DateTimeOffset startedAt,
        GameTrackerConnection tracker
    )
    {
        Process = new ManagedProcess(nativeProcess, memoryMb, initialOutOfMemoryKillCount, outputTask);
        Version = version;
        StartedAt = startedAt;
        Tracker = tracker;
    }

    public IManagedProcess Process { get; }

    public DateTimeOffset StartedAt { get; }

    public GameTrackerConnection Tracker { get; }

    public string Version { get; }

    public void Dispose()
    {
        Process.Dispose();
    }
}
