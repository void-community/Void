using System.Diagnostics;
using System.Threading.Channels;

using Void.Client.Abstractions;

namespace Void.Client.Models;

internal sealed record RunningGame : IDisposable
{
    public RunningGame(
        IManagedProcess process,
        string version,
        DateTimeOffset startedAt,
        GameTrackerConnection tracker,
        ChannelReader<string>? warnings = null
    )
    {
        Process = process;
        Version = version;
        StartedAt = startedAt;
        Tracker = tracker;
        Warnings = warnings ?? NoWarnings;
    }

    public RunningGame(
        Process nativeProcess,
        int? memoryMb,
        long? initialOutOfMemoryKillCount,
        Task outputTask,
        ChannelReader<string> warnings,
        GameErrorLog errors,
        string version,
        DateTimeOffset startedAt,
        GameTrackerConnection tracker,
        string minecraftDirectory
    )
    {
        Process = new ManagedProcess(
            nativeProcess,
            memoryMb,
            initialOutOfMemoryKillCount,
            outputTask,
            minecraftDirectory: minecraftDirectory,
            launchedAt: startedAt.UtcDateTime,
            errors: errors
        );
        Version = version;
        StartedAt = startedAt;
        Tracker = tracker;
        Warnings = warnings;
    }

    public static ChannelReader<string> NoWarnings { get; } = CreateNoWarnings();

    public IManagedProcess Process { get; }

    public DateTimeOffset StartedAt { get; }

    public GameTrackerConnection Tracker { get; }

    public string Version { get; }

    /// <summary>Non-fatal problems reported on the game's output, such as native dialogs the image answered.</summary>
    public ChannelReader<string> Warnings { get; }

    public void Dispose()
    {
        Process.Dispose();
    }

    private static ChannelReader<string> CreateNoWarnings()
    {
        Channel<string> channel = Channel.CreateUnbounded<string>();
        channel.Writer.Complete();

        return channel.Reader;
    }
}
