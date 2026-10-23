using System.Text;

namespace Void.Client;

/// <summary>Finds the crash report a game saved, or the error log its Java runtime wrote, and reads what it says about the crash.</summary>
internal static class CrashReports
{
    private const string DescriptionPrefix = "Description:";
    // System Details follow this heading and list the JVM arguments, which include the agent's token, so reading stops there.
    private const string DetailsHeading = "A detailed walkthrough";
    // Forge and NeoForge describe each mod that failed to load in a section of its own, below the details heading.
    private const string FailureMessagePrefix = "\tFailure message:";
    private const string JavaErrorHeading = "A fatal error has been detected by the Java Runtime Environment:";
    private const string JavaErrorReasonsHeading = "Possible reasons:";
    private const string JavaProblematicFrameHeading = "Problematic frame:";
    private const int MaximumCauseLength = 1024;
    private const int MaximumReadBytes = 32 * 1024;
    private const string SystemDetailsHeading = "-- System Details --";

    /// <summary>Tells whether a path is a crash report the game saved in its crash-reports directory since it launched.</summary>
    public static bool IsReport(string reportPath, string minecraftDirectory, DateTime launchedAt)
    {
        try
        {
            string directory = Path.GetFullPath(minecraftDirectory);
            string crashReportsDirectory = $"{Path.Join(directory, path2: "crash-reports")}{Path.DirectorySeparatorChar}";
            FileInfo report = new(Path.GetFullPath(reportPath, directory));

            return report.FullName.StartsWith(crashReportsDirectory, StringComparison.Ordinal) && IsSavedSince(report, launchedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads what the game's crash report, or else its Java runtime's error log, saved since it launched says about the crash.</summary>
    /// <returns>The cause, or <see langword="null"/> when the game saved neither.</returns>
    public static string? ReadCrashCause(string minecraftDirectory, DateTime launchedAt)
    {
        // The Java runtime can also fail while Minecraft exits after a crash, and the crash report tells more.
        if (FindNewest(Path.Join(minecraftDirectory, path2: "crash-reports"), searchPattern: "crash-*.txt", launchedAt) is { } report)
            return Read(report, Summarize) ?? $"Minecraft crashed, see crash-reports/{Path.GetFileName(report)}";

        // The Java runtime writes its error log to its working directory, the game directory, when it fails outside of Java code.
        return FindNewest(minecraftDirectory, searchPattern: "hs_err_pid*.log", launchedAt) is { } log
            ? Read(log, SummarizeJavaError) ?? $"Minecraft's Java runtime crashed, see {Path.GetFileName(log)}"
            : null;
    }

    internal static string Limit(string cause)
    {
        if (cause.Length <= MaximumCauseLength)
            return cause;

        int length = MaximumCauseLength - 1;

        if (char.IsHighSurrogate(cause[length - 1]))
            length--;

        return $"{cause[..length]}…";
    }

    /// <summary>
    /// Describes a crash in one line from its report: the description and the error up to its first stack frame, such as
    /// "Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash". The error of a mod
    /// loading report is a placeholder, so the failure messages of its mods take its place.
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
        List<string> failures = ReadFailureMessages(report);

        return failures.Count is not 0
            ? Limit($"{crash}: {string.Join(separator: "; ", failures)}")
            : Limit(error.Count is 0 ? crash : $"{crash}: {string.Join(separator: ' ', error)}");
    }

    /// <summary>
    /// Describes a crash of the Java runtime in one line from the header of its error log: the error and the native frame it happened
    /// in, such as "Minecraft's Java runtime crashed: SIGSEGV (0xb) at pc=0x0000ffffa4f7f228, pid=1008, tid=1008 in C [libc.so.6+0x8f228]".
    /// </summary>
    /// <returns>The description, or <see langword="null"/> when the log has no header.</returns>
    internal static string? SummarizeJavaError(string log)
    {
        List<List<string>> paragraphs = [[]];

        foreach (string logLine in log.Split(separator: '\n'))
        {
            string line = logLine.TrimEnd(trimChar: '\r');

            // The header is commented, and the summary after it lists the JVM arguments, which include the agent's token.
            if (!line.StartsWith(value: '#'))
                break;

            string text = string.Join(separator: ' ', line.TrimStart(trimChar: '#').Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries));

            if (text.Length is not 0)
                paragraphs[^1].Add(text);
            else if (paragraphs[^1].Count is not 0)
                paragraphs.Add([]);
        }

        // A paragraph only starts after a non-empty one, so only the last can be empty.
        if (paragraphs[^1].Count is 0)
            paragraphs.RemoveAt(paragraphs.Count - 1);

        if (paragraphs.Count is 0)
            return null;

        // The error follows the heading, except for native memory exhaustion, which comes first with possible reasons after it.
        int heading = paragraphs.FindIndex(static paragraph => paragraph[index: 0] == JavaErrorHeading);
        List<string> error = heading < 0 ? paragraphs[index: 0] : heading + 1 < paragraphs.Count ? paragraphs[heading + 1] : [];
        int reasons = error.IndexOf(JavaErrorReasonsHeading);
        string? frame = paragraphs.SelectMany(static paragraph => paragraph).SkipWhile(static line => line != JavaProblematicFrameHeading).Skip(count: 1).FirstOrDefault();
        string crash = $"Minecraft's Java runtime crashed: {string.Join(separator: ' ', reasons < 0 ? error : error[..reasons])}";

        return Limit(frame is null ? crash : $"{crash} in {frame}");
    }

    private static string? FindNewest(string directory, string searchPattern, DateTime launchedAt)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, searchPattern)
                    .Select(static file => new FileInfo(file))
                    .Where(file => IsSavedSince(file, launchedAt))
                    .OrderByDescending(static file => file.LastWriteTimeUtc)
                    .ThenByDescending(static file => file.Name, StringComparer.Ordinal)
                    .FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsSavedSince(FileInfo file, DateTime launchedAt)
    {
        return file.Exists && file.LinkTarget is null && file.LastWriteTimeUtc >= launchedAt;
    }

    private static string? Read(string path, Func<string, string?> summarize)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            byte[] buffer = new byte[MaximumReadBytes];
            int length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            return summarize(Encoding.UTF8.GetString(buffer, index: 0, length));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file's name still tells where the cause is.
            Console.Error.WriteLine($"Reading {path} failed: {exception.Message}");

            return null;
        }
    }

    private static List<string> ReadFailureMessages(string report)
    {
        List<string> messages = [];
        bool continued = false;

        foreach (string reportLine in report.Split(separator: '\n'))
        {
            string line = reportLine.TrimEnd(trimChar: '\r');

            // System Details list the JVM arguments, which include the agent's token, so reading stops there.
            if (line.StartsWith(SystemDetailsHeading, StringComparison.Ordinal))
                break;

            // A message's later lines are indented one tab further.
            if (line.StartsWith(FailureMessagePrefix, StringComparison.Ordinal))
            {
                messages.Add(line[FailureMessagePrefix.Length..].Trim());
                continued = true;
            }
            else if (continued && line.StartsWith(value: "\t\t", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    messages[^1] = $"{messages[^1]} {line.Trim()}";
            }
            else
            {
                continued = false;
            }
        }

        return messages;
    }
}
