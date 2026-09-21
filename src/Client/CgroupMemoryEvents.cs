using System.Globalization;

namespace Void.Client;

internal static class CgroupMemoryEvents
{
    private const string MemoryEventsPath = "/sys/fs/cgroup/memory.events";

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
