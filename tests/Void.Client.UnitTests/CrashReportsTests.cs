using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class CrashReportsTests
{
    private const string DebugCrash = "Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash";

    private const string Walkthrough = """


        A detailed walkthrough of the error, its code path and all known details is as follows:
        ---------------------------------------------------------------------------------------

        -- System Details --
        	JVM Flags: 2 total; -Xmx2048M -javaagent:/opt/portableminecraftclient/void-client-agent.jar=descriptor=ZA;token=secret-token
        """;

    public static TheoryData<string, string> Reports => new()
    {
        {
            // Minecraft 26.1
            """
            ---- Minecraft Crash Report ----
            // Who set us up the TNT?

            Time: 2026-10-20 08:28:40
            Description: Manually triggered debug crash

            java.lang.Throwable: Manually triggered debug crash
            	at net.minecraft.client.KeyboardHandler.tick(KeyboardHandler.java:653)
            	at net.minecraft.client.Minecraft.tick(Minecraft.java:2061)
            """ + Walkthrough,
            DebugCrash
        },
        {
            // Minecraft 1.7.10, written on Windows
            "---- Minecraft Crash Report ----\r\n// Oops.\r\n\r\nTime: 10/20/26 8:28 AM\r\nDescription: Manually triggered debug crash\r\n\r\n"
            + "java.lang.Throwable: Manually triggered debug crash\r\n\tat bao.p(SourceFile:1517)\r\n\tat bao.f(SourceFile:990)\r\n",
            DebugCrash
        },
        {
            // Forge 1.12 lists coremods above the description
            """
            ---- Minecraft Crash Report ----

            WARNING: coremods are present:
              LoadingPlugin (example.jar)
            Contact their authors BEFORE contacting forge

            // Don't do that.

            Time: 10/20/26 8:28 AM
            Description: There was a severe problem during mod loading that has caused the game to fail

            net.minecraftforge.fml.common.LoaderExceptionModCrash: Caught exception from Example Mod (examplemod)
            	at net.minecraftforge.fml.common.LoadController.transition(LoadController.java:178)
            Caused by: java.lang.IllegalStateException: Example Mod refuses to load
            	at example.ExampleMod.preInit(ExampleMod.java:12)
            """,
            "Minecraft crashed (There was a severe problem during mod loading that has caused the game to fail): "
            + "net.minecraftforge.fml.common.LoaderExceptionModCrash: Caught exception from Example Mod (examplemod)"
        },
        {
            // NeoForge 26.1, a mod that failed to construct
            """
            ---- Minecraft Crash Report ----
            // Ouch. That hurt :(

            Time: 2026-10-20 06:19:30
            Description: Bootstrap

            net.neoforged.fml.ModLoadingException: Loading errors encountered:
            	- Just Enough Items (jei) has failed to load correctly
            	  java.lang.NoClassDefFoundError: Could not initialize class com.stal111.forbidden_arcanus.ForbiddenArcanus
            	- Forbidden Arcanus (forbidden_arcanus) has failed to load correctly
            	  java.lang.ExceptionInInitializerError

            	at TRANSFORMER/neoforge@26.1.2.122/net.neoforged.neoforge.client.loading.ClientModLoader.begin(ClientModLoader.java:86)
            """ + Walkthrough,
            "Minecraft crashed (Bootstrap): net.neoforged.fml.ModLoadingException: Loading errors encountered: "
            + "- Just Enough Items (jei) has failed to load correctly java.lang.NoClassDefFoundError: Could not initialize class com.stal111.forbidden_arcanus.ForbiddenArcanus "
            + "- Forbidden Arcanus (forbidden_arcanus) has failed to load correctly java.lang.ExceptionInInitializerError"
        },
        {
            // NeoForge 26.1, saved by the agent for a missing dependency
            """
            ---- Minecraft Crash Report ----
            // The Void client saved this report in place of NeoForge's fatal error window, which nobody can answer in the container.

            Time: 2026-10-20T08:44:05Z
            Description: Mod loading error has occurred

            net.neoforged.fml.ModLoadingException: Loading errors encountered:
            	- Mod missingstub requires nonexistentmod 1 or above
            	  Currently, nonexistentmod is not installed

            	at net.neoforged.fml.loading.FMLLoader.loadMods(FMLLoader.java:529)
            """,
            "Minecraft crashed (Mod loading error has occurred): net.neoforged.fml.ModLoadingException: Loading errors encountered: "
            + "- Mod missingstub requires nonexistentmod 1 or above Currently, nonexistentmod is not installed"
        },
        {
            // An error without stack frames ends before the details, which list the agent's token
            "---- Minecraft Crash Report ----\nDescription: Unexpected error\n\njava.lang.OutOfMemoryError: Java heap space" + Walkthrough,
            "Minecraft crashed (Unexpected error): java.lang.OutOfMemoryError: Java heap space"
        },
        {
            "---- Minecraft Crash Report ----\nDescription: Rendering overlay\n" + Walkthrough,
            "Minecraft crashed (Rendering overlay)"
        }
    };

    [Theory]
    [MemberData(nameof(Reports))]
    public void SummaryHasTheDescriptionAndTheErrorUpToItsFirstFrame(string report, string expected)
    {
        string? summary = CrashReports.Summarize(report);

        Assert.Equal(expected, summary);
        Assert.DoesNotContain("secret-token", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportWithoutDescriptionIsNamed()
    {
        string minecraftDirectory = Directory.CreateTempSubdirectory().FullName;

        try
        {
            string report = Path.Join(Directory.CreateDirectory(Path.Join(minecraftDirectory, "crash-reports")).FullName, "crash-2026-10-20_08.28.40-client.txt");
            File.WriteAllText(report, "Not a crash report");

            Assert.Null(CrashReports.Summarize("Not a crash report"));
            Assert.Equal("Minecraft crashed, see crash-reports/crash-2026-10-20_08.28.40-client.txt", CrashReports.ReadCause(report));
        }
        finally
        {
            Directory.Delete(minecraftDirectory, recursive: true);
        }
    }

    [Fact]
    public void LongErrorsAreCutWithoutSplittingCharacters()
    {
        // The emoji is a surrogate pair whose halves would straddle the cut after 1023 characters.
        const string prefix = "Minecraft crashed (Unexpected error): java.lang.IllegalStateException: ";
        string report = $"Description: Unexpected error\n\njava.lang.IllegalStateException: {new string('x', 1022 - prefix.Length)}🧨{new string('y', 100)}\n";

        string? summary = CrashReports.Summarize(report);

        Assert.NotNull(summary);
        Assert.Equal(1023, summary.Length);
        Assert.EndsWith("x…", summary, StringComparison.Ordinal);
        Assert.Equal(1024, CrashReports.Summarize(report.Replace("🧨", "zz", StringComparison.Ordinal))?.Length);
    }

    [Fact]
    public void OnlyReportsTheGameSavedSinceItLaunchedCount()
    {
        // Chat reaches the same output as the crash marker, so a path a player types must not count.
        string minecraftDirectory = Directory.CreateTempSubdirectory().FullName;

        try
        {
            string crashReports = Directory.CreateDirectory(Path.Join(minecraftDirectory, "crash-reports")).FullName;
            DateTime launchedAt = DateTime.UtcNow.AddMinutes(-1);
            string earlier = Path.Join(crashReports, "crash-2026-10-20_06.00.00-client.txt");
            string report = Path.Join(crashReports, "crash-2026-10-20_06.19.30-client.txt");
            string newest = Path.Join(crashReports, "crash-2026-10-20_06.19.31-fml.txt");
            string notes = Path.Join(crashReports, "notes.txt");
            string link = Path.Join(crashReports, "crash-2026-10-20_06.19.32-client.txt");
            string optionsFile = Path.Join(minecraftDirectory, "options.txt");
            File.WriteAllText(earlier, "Description: Earlier crash");
            File.SetLastWriteTimeUtc(earlier, launchedAt.AddMinutes(-5));
            File.WriteAllText(report, "Description: Crash");
            File.SetLastWriteTimeUtc(report, launchedAt.AddSeconds(10));
            File.WriteAllText(newest, "Description: Newest crash");
            File.SetLastWriteTimeUtc(newest, launchedAt.AddSeconds(20));
            File.WriteAllText(notes, "Description: Notes");
            File.WriteAllText(optionsFile, "fullscreen:false");
            File.CreateSymbolicLink(link, optionsFile);

            Assert.True(CrashReports.IsReport(report, minecraftDirectory, launchedAt));
            Assert.True(CrashReports.IsReport("crash-reports/crash-2026-10-20_06.19.30-client.txt", minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport(earlier, minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport(Path.Join(crashReports, "crash-2026-10-20_06.20.00-client.txt"), minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport(optionsFile, minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport(Path.Join(crashReports, "..", "options.txt"), minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport(link, minecraftDirectory, launchedAt));
            Assert.False(CrashReports.IsReport("\0", minecraftDirectory, launchedAt));

            Assert.Equal(newest, CrashReports.Find(minecraftDirectory, launchedAt));
            Assert.Null(CrashReports.Find(minecraftDirectory, DateTime.UtcNow.AddMinutes(1)));
            Assert.Null(CrashReports.Find(Path.Join(minecraftDirectory, "missing"), launchedAt));
        }
        finally
        {
            Directory.Delete(minecraftDirectory, recursive: true);
        }
    }
}
