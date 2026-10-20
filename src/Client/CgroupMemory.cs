using System.Globalization;

namespace Void.Client;

internal static class CgroupMemory
{
    private const string MemoryEventsPath = "/sys/fs/cgroup/memory.events";
    private const string MemoryMaximumPath = "/sys/fs/cgroup/memory.max";

    /// <summary>Reads the container's memory limit in MiB, or <see langword="null"/> when it has none or it can't be read.</summary>
    public static long? ReadMemoryLimitMb()
    {
        try
        {
            return ParseMemoryLimitMb(File.ReadAllText(MemoryMaximumPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static long? ReadOutOfMemoryKillCount()
    {
        try
        {
            return ParseOutOfMemoryKillCount(File.ReadAllText(MemoryEventsPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The file holds the limit in bytes, or "max" when there is none.
    internal static long? ParseMemoryLimitMb(string content)
    {
        return long.TryParse(content.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long bytes) ? bytes / (1024 * 1024) : null;
    }

    internal static long? ParseOutOfMemoryKillCount(string content)
    {
        foreach (string line in content.Split(separator: '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts is ["oom_kill", var value] && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long count))
                return count;
        }

        return null;
    }
}
