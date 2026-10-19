using System.Threading.Channels;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class GameWarningTests
{
    private const string DialogWarning = "Dialog \"Memory Settings\" answered with Cancel: You have allocated less memory( 5736 MB) than the recommended minimum";

    [Fact]
    public async Task OutputPumpReportsWarningLinesSplitAcrossReadsAndKeepsConsoleOutput()
    {
        string text = "[00:00:29] [modloading-worker-0/INFO] loading\n"
                      + $"Void client warning: {DialogWarning}\r\n"
                      + "not a Void client warning: line\n"
                      + "Void client warning: unfinished";
        using var reader = new ChunkedReader(text);
        using var console = new StringWriter();
        var warnings = Channel.CreateUnbounded<string>();

        await new GameRuntime().PumpOutputAsync(reader, console, sessionId: null, stream: "stderr", warnings.Writer, TestContext.Current.CancellationToken);

        Assert.Equal(text.Replace("\r\n", "\n", StringComparison.Ordinal), console.ToString());
        Assert.True(warnings.Reader.TryRead(out string? warning));
        Assert.Equal(DialogWarning, warning);
        Assert.False(warnings.Reader.TryRead(out _));
    }

    [Fact]
    public void StopsBufferingOverlongUnfinishedLines()
    {
        var warnings = Channel.CreateUnbounded<string>();

        Assert.Equal("Void client warning: short", GameRuntime.ReadGameWarnings("Void client warning: short", warnings.Writer));
        Assert.Equal("", GameRuntime.ReadGameWarnings(new string('x', 70_000), warnings.Writer));
    }

    [Fact]
    public void TracksWindowsOfOtherProcessesSinceTheyAppeared()
    {
        var start = DateTimeOffset.UnixEpoch;
        GameRuntime.VisibleWindow game = new("16777225", "Minecraft NeoForge* 1.21.1", ProcessId: 100);
        GameRuntime.VisibleWindow dialog = new("14680103", "Memory Settings");
        GameRuntime.VisibleWindow crashReporter = new("14680071", "Crash Assistant", ProcessId: 200);

        var first = GameRuntime.TrackForeignWindows([game, dialog, crashReporter], gameProcessId: 100, new Dictionary<string, DateTimeOffset>(), start);
        Assert.Equal(new Dictionary<string, DateTimeOffset> { [dialog.Id] = start, [crashReporter.Id] = start }, first);

        var later = GameRuntime.TrackForeignWindows([game, dialog], gameProcessId: 100, first, start.AddSeconds(10));
        Assert.Equal(new Dictionary<string, DateTimeOffset> { [dialog.Id] = start }, later);

        var reopened = GameRuntime.TrackForeignWindows([game, dialog, crashReporter], gameProcessId: 100, later, start.AddSeconds(12));
        Assert.Equal(start.AddSeconds(12), reopened[crashReporter.Id]);
    }

    [Fact]
    public void TracksNothingWhileTheGameProcessIsUnknown()
    {
        Assert.Empty(GameRuntime.TrackForeignWindows([new("14680103", "Memory Settings")], gameProcessId: null, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UnixEpoch));
    }

    private sealed class ChunkedReader(string text) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 23)], cancellationToken);
    }
}
