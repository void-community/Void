using System.Text.RegularExpressions;

namespace Void.Client;

/// <summary>
/// Keeps the last error entry the game logged, so a game that logs why it ends and exits without a crash report, such as a mod that
/// refuses the installed LWJGL and calls System.exit, still tells why. Entries are read from PortableMC's "[12:00:00] [main] [ERROR]"
/// lines, and the lines that follow one belong to it. One output reads the lines, while the last error can be read at any time.
/// </summary>
internal sealed partial class GameErrorLog
{
    private const int MaximumLines = 20;
    // Modpacks log plenty of harmless errors, so only one logged shortly before the game exited is taken as its last error.
    private static readonly TimeSpan ExitWindow = TimeSpan.FromSeconds(seconds: 10);
    private bool _collecting;
    private LoggedError? _lastError;

    /// <summary>Reads one complete line of the game's output.</summary>
    public void Read(ReadOnlySpan<char> line, DateTime readAt)
    {
        var headers = EntryHeader().EnumerateMatches(line);

        if (headers.MoveNext())
        {
            var entry = line[headers.Current.Length..];
            _collecting = entry.StartsWith(value: "ERROR] ", StringComparison.Ordinal) || entry.StartsWith(value: "FATAL] ", StringComparison.Ordinal);

            if (_collecting)
                Volatile.Write(ref _lastError, new LoggedError(entry["ERROR] ".Length..].Trim().ToString(), Lines: 1, readAt));

            return;
        }

        var lastError = Volatile.Read(ref _lastError);

        // Like a crash report, the error ends at its first stack frame.
        if (lastError is null || lastError.Lines >= MaximumLines || line.StartsWith(value: "\tat ", StringComparison.Ordinal))
            _collecting = false;
        else if (_collecting && !line.IsWhiteSpace())
            Volatile.Write(ref _lastError, lastError with { Text = $"{lastError.Text} {line.Trim()}", Lines = lastError.Lines + 1 });
    }

    /// <summary>Gets the last error the game logged shortly before it exited at <paramref name="exitedAt"/>, or <see langword="null"/> without one.</summary>
    public string? ReadLastError(DateTime exitedAt)
    {
        var lastError = Volatile.Read(ref _lastError);

        return lastError is null || exitedAt - lastError.LoggedAt > ExitWindow ? null : CrashReports.Limit(lastError.Text);
    }

    [GeneratedRegex(pattern: @"^\[\d\d:\d\d:\d\d\] \[[^\]]*\] \[")]
    private static partial Regex EntryHeader();

    private sealed record LoggedError(string Text, int Lines, DateTime LoggedAt);
}
