using System.Threading.Channels;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class GameErrorLogTests
{
    private static readonly DateTime LoggedAt = new(2026, 10, 23, 13, 5, 47, DateTimeKind.Utc);

    [Fact]
    public async Task KeepsTheLastErrorTheGameLoggedBeforeItExited()
    {
        // Issue #1620: Sodium logs why it refuses the installed LWJGL and calls System.exit without a crash report.
        string text = "[13:05:40] [main] [ERROR] Harmless error while loading\n"
                      + "[13:05:47] [main] [ERROR] The game failed to start because the currently active LWJGL version is not compatible.\n"
                      + "Installed version: 3.3.3-snapshot\r\n"
                      + "\n"
                      + "Required version: 3.3.1\n"
                      + "[13:05:47] [main] [INFO] Stopping\n"
                      + "not part of the error\n";
        using var reader = new StringReader(text);
        using var console = new StringWriter();
        var warnings = Channel.CreateUnbounded<string>();
        GameErrorLog errors = new();

        await new GameRuntime().PumpOutputAsync(reader, console, sessionId: null, stream: "stdout", warnings.Writer, errors: errors, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            "The game failed to start because the currently active LWJGL version is not compatible. Installed version: 3.3.3-snapshot Required version: 3.3.1",
            errors.ReadLastError(DateTime.UtcNow)
        );
    }

    [Fact]
    public void EndsTheErrorAtItsFirstStackFrame()
    {
        GameErrorLog errors = Read(
            "[10:04:39] [Render thread] [FATAL] java.lang.IllegalArgumentException: Duplicate handler name: forge:forge_fixes",
            "\tat io.netty.channel.DefaultChannelPipeline.checkDuplicateName(DefaultChannelPipeline.java:1066)",
            "Caused by: java.lang.IllegalStateException"
        );

        Assert.Equal("java.lang.IllegalArgumentException: Duplicate handler name: forge:forge_fixes", errors.ReadLastError(LoggedAt));
    }

    [Fact]
    public void IgnoresAnErrorLoggedLongBeforeTheExit()
    {
        GameErrorLog errors = Read("[13:05:47] [Download-2] [ERROR] com.mojang.authlib.exceptions.InvalidCredentialsException: Status: 401");

        Assert.NotNull(errors.ReadLastError(LoggedAt.AddSeconds(10)));
        Assert.Null(errors.ReadLastError(LoggedAt.AddSeconds(11)));
    }

    [Fact]
    public void KeepsNothingWithoutErrors()
    {
        GameErrorLog errors = Read("[13:05:47] [main] [WARN] Unrecognized user type:", "Caused by: nothing", "[  OK  ] Launched");

        Assert.Null(errors.ReadLastError(LoggedAt));
    }

    private static GameErrorLog Read(params string[] lines)
    {
        GameErrorLog errors = new();

        foreach (string line in lines)
            errors.Read(line, LoggedAt);

        return errors;
    }
}
