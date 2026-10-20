using System.Text;

namespace Void.Client;

/// <summary>Finds the crash report a game saved and reads what it says about the crash.</summary>
internal static class CrashReports
{
    private const string DescriptionPrefix = "Description:";
    // System Details follow this heading and list the JVM arguments, which include the agent's token, so reading stops there.
    private const string DetailsHeading = "A detailed walkthrough";
    private const int MaximumCauseLength = 1024;
    private const int MaximumReadBytes = 32 * 1024;

    /// <summary>Finds the newest crash report the game saved since it launched.</summary>
    /// <returns>The report's path, or <see langword="null"/> when the game saved none.</returns>
    public static string? Find(string minecraftDirectory, DateTime launchedAt)
    {
        string directory = Path.Join(minecraftDirectory, path2: "crash-reports");

        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, searchPattern: "crash-*.txt")
                    .Where(report => IsReport(report, minecraftDirectory, launchedAt))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ThenByDescending(report => report, StringComparer.Ordinal)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Tells whether a path is a crash report the game saved in its crash-reports directory since it launched.</summary>
    public static bool IsReport(string reportPath, string minecraftDirectory, DateTime launchedAt)
    {
        try
        {
            string directory = Path.GetFullPath(minecraftDirectory);
            string crashReportsDirectory = $"{Path.Join(directory, path2: "crash-reports")}{Path.DirectorySeparatorChar}";
            FileInfo report = new(Path.GetFullPath(reportPath, directory));

            return report.FullName.StartsWith(crashReportsDirectory, StringComparison.Ordinal) && report.Exists && report.LinkTarget is null
                   && report.LastWriteTimeUtc >= launchedAt;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads what a crash report says about the crash, see <see cref="Summarize"/>.</summary>
    public static string ReadCause(string reportPath)
    {
        try
        {
            using FileStream stream = new(reportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            byte[] buffer = new byte[MaximumReadBytes];
            int length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            if (Summarize(Encoding.UTF8.GetString(buffer, index: 0, length)) is { } cause)
                return cause;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The report's name still tells where the cause is.
            Console.Error.WriteLine($"Reading crash report {reportPath} failed: {exception.Message}");
        }

        return $"Minecraft crashed, see crash-reports/{Path.GetFileName(reportPath)}";
    }

    /// <summary>
    /// Describes a crash in one line from its report: the description and the error up to its first stack frame, such as
    /// "Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash".
    /// </summary>
    /// <returns>The description, or <see langword="null"/> when the report has no description.</returns>
    internal static string? Summarize(string report)
    {
        string? description = null;
        List<string> error = [];

        foreach (string reportLine in report.Split(separator: '\n'))
        {
            string line = reportLine.TrimEnd(trimChar: '\r');

            if (line.StartsWith(DetailsHeading, StringComparison.Ordinal))
                break;

            // Some loaders put warnings above the description, so it is searched for rather than expected on a given line.
            if (description is null)
            {
                if (line.StartsWith(DescriptionPrefix, StringComparison.Ordinal))
                    description = line[DescriptionPrefix.Length..].Trim();

                continue;
            }

            if (line.StartsWith(value: "\tat ", StringComparison.Ordinal) || (string.IsNullOrWhiteSpace(line) && error.Count is not 0))
                break;

            if (!string.IsNullOrWhiteSpace(line))
                error.Add(line.Trim());
        }

        if (description is null)
            return null;

        string crash = description.Length is 0 ? "Minecraft crashed" : $"Minecraft crashed ({description})";
        string cause = error.Count is 0 ? crash : $"{crash}: {string.Join(separator: ' ', error)}";

        if (cause.Length <= MaximumCauseLength)
            return cause;

        int length = MaximumCauseLength - 1;

        if (char.IsHighSurrogate(cause[length - 1]))
            length--;

        return $"{cause[..length]}…";
    }
}
