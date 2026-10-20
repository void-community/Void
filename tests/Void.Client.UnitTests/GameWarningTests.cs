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

        await new GameRuntime().PumpOutputAsync(reader, console, sessionId: null, stream: "stderr", warnings.Writer, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(text.Replace("\r\n", "\n", StringComparison.Ordinal), console.ToString());
        Assert.True(warnings.Reader.TryRead(out string? warning));
        Assert.Equal(DialogWarning, warning);
        Assert.False(warnings.Reader.TryRead(out _));
    }

    [Fact]
    public async Task OutputPumpReportsCrashReportsBehindAnyPrefix()
    {
        string text = "[06:19:32] [main/INFO]: [net.minecraft.server.Bootstrap:realStdoutPrintln:140]: #@!@# Game crashed! Crash report saved to: #@!@# /root/.minecraft/crash-reports/crash-2026-10-20_06.19.30-client.txt\n"
                      + "[06:33:50] [_stdout_] [INFO] #@!@# Game crashed! Crash report saved to: #@!@# /root/.minecraft/crash-reports/crash-2026-10-20_06.33.50-client.txt\r\n"
                      + "#@?@# Game crashed! Crash report could not be saved. #@?@#\n"
                      + "[06:34:00] [Render thread/INFO]: [CHAT] <Player> Game crashed!\n";
        using var reader = new ChunkedReader(text);
        using var console = new StringWriter();
        var warnings = Channel.CreateUnbounded<string>();
        List<string> reports = [];

        await new GameRuntime().PumpOutputAsync(reader, console, sessionId: null, stream: "stdout", warnings.Writer, reports.Add, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["/root/.minecraft/crash-reports/crash-2026-10-20_06.19.30-client.txt", "/root/.minecraft/crash-reports/crash-2026-10-20_06.33.50-client.txt"], reports);
        Assert.False(warnings.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("[12:37:42] [main/INFO]: Setting user: Player\n", 2)]
    public async Task OutputPumpReportsEveryRead(string text, int reads)
    {
        // Issue #1610: a start waits for the game's window only while the game keeps printing.
        using var reader = new ChunkedReader(text);
        using var console = new StringWriter();
        int reported = 0;

        await new GameRuntime().PumpOutputAsync(
            reader,
            console,
            sessionId: null,
            stream: "stdout",
            outputRead: () => reported++,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Equal(reads, reported);
    }

    [Fact]
    public void SilenceOfOutputThatEndedNeverCancels()
    {
        // The silence token outlives the output it watches, and a start can link to it after the output ended.
        CancellationTokenSource outputSilence = new(TimeSpan.FromMilliseconds(milliseconds: 1));
        var token = outputSilence.Token;
        outputSilence.Dispose();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, token);
        Thread.Sleep(millisecondsTimeout: 50);

        Assert.False(token.IsCancellationRequested);
        Assert.False(window.IsCancellationRequested);
    }

    [Fact]
    public void StopsBufferingOverlongUnfinishedLines()
    {
        var warnings = Channel.CreateUnbounded<string>();

        Assert.Equal("Void client warning: short", GameRuntime.ReadGameOutput("Void client warning: short", warnings.Writer));
        Assert.Equal("", GameRuntime.ReadGameOutput(new string('x', 70_000), warnings.Writer));
    }

    [Fact]
    public void TracksWindowsOfOtherProcessesSinceTheyAppeared()
    {
        var start = DateTimeOffset.UnixEpoch;
        GameRuntime.VisibleWindow game = new("16777225", "Minecraft NeoForge* 1.21.1", ProcessId: 100);
        GameRuntime.VisibleWindow dialog = new("14680103", "Memory Settings");
        GameRuntime.VisibleWindow crashReporter = new("14680071", "Crash Assistant", ProcessId: 200);

        var first = GameRuntime.TrackForeignWindows([game, dialog, crashReporter], gameProcessId: 100, game.Id, new Dictionary<string, DateTimeOffset>(), start);
        Assert.Equal(new Dictionary<string, DateTimeOffset> { [dialog.Id] = start, [crashReporter.Id] = start }, first);

        var later = GameRuntime.TrackForeignWindows([game, dialog], gameProcessId: 100, game.Id, first, start.AddSeconds(10));
        Assert.Equal(new Dictionary<string, DateTimeOffset> { [dialog.Id] = start }, later);

        var reopened = GameRuntime.TrackForeignWindows([game, dialog, crashReporter], gameProcessId: 100, game.Id, later, start.AddSeconds(12));
        Assert.Equal(start.AddSeconds(12), reopened[crashReporter.Id]);
    }

    [Fact]
    public void TheGameWindowOfLwjgl2IsNotForeignWithoutAProcess()
    {
        // LWJGL2 games (Minecraft 1.12 and older) don't set _NET_WM_PID, so only the window id tells their window apart.
        GameRuntime.VisibleWindow game = new("16777218", "Minecraft 1.10");
        GameRuntime.VisibleWindow dialog = new("14680103", "Memory Settings");

        var foreign = GameRuntime.TrackForeignWindows([game, dialog], gameProcessId: 100, game.Id, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UnixEpoch);

        Assert.Equal([dialog.Id], foreign.Keys);
    }

    [Fact]
    public void TracksNothingWhileTheGameProcessIsUnknown()
    {
        Assert.Empty(GameRuntime.TrackForeignWindows([new("14680103", "Memory Settings")], gameProcessId: null, gameWindowId: null, new Dictionary<string, DateTimeOffset>(), DateTimeOffset.UnixEpoch));
    }

    private sealed class ChunkedReader(string text) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 23)], cancellationToken);
    }
}
