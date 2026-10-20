using System.Runtime.Versioning;
using Void.Client;
using Void.Client.Failures;
using Xunit;

namespace Void.Client.UnitTests;

[UnsupportedOSPlatform("windows")]
public sealed class PreparationScriptTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    private string Scripts => Path.Join(_directory, "prepare.d");

    private string Log => Path.Join(_directory, "prepare.log");

    [Fact]
    public async Task ScriptsRunInNameOrderWithTheGameTheyPrepare()
    {
        // Issue #1618: images built on the client add files for the exact Minecraft version, as the demo adds Sodium.
        WriteScript("20-second", $"echo \"second $MINECRAFT_LOADER $MINECRAFT_VERSION $MINECRAFT_DIRECTORY\" >> '{Log}'");
        WriteScript("10-first", $"echo \"first $MINECRAFT_LOADER $MINECRAFT_VERSION $MINECRAFT_DIRECTORY\" >> '{Log}'");

        await RunAsync("neoforge::21.1.200", "1.21.1");
        await RunAsync("mojang:1.21.1", "1.21.1");

        Assert.Equal(
            ["first neoforge 1.21.1 /game", "second neoforge 1.21.1 /game", "first vanilla 1.21.1 /game", "second vanilla 1.21.1 /game"],
            await File.ReadAllLinesAsync(Log, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task FailingScriptFailsTheStartAndStopsTheRest()
    {
        WriteScript("10-fail", "echo preparing; echo boom >&2; exit 3");
        WriteScript("20-after", $"echo after >> '{Log}'");

        var failure = await Assert.ThrowsAsync<GameClientException>(() => RunAsync("neoforge::21.1.200", "1.21.1"));

        Assert.Equal("client.install.failed", failure.Failure.Code);
        Assert.Equal("minecraft", failure.Failure.Stage);
        Assert.Equal("Preparation script 10-fail failed with exit code 3: boom", failure.Failure.Message);
        Assert.False(File.Exists(Log));
    }

    [Fact]
    public async Task ScriptThatCannotRunFailsTheStart()
    {
        Directory.CreateDirectory(Scripts);
        await File.WriteAllTextAsync(Path.Join(Scripts, "10-not-executable"), "#!/bin/sh\nexit 0\n", TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<GameClientException>(() => RunAsync("mojang:1.21.1", "1.21.1"));

        Assert.Equal("client.install.failed", failure.Failure.Code);
        Assert.StartsWith("Preparation script 10-not-executable couldn't run:", failure.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDirectoryRunsNothing()
    {
        await RunAsync("mojang:1.21.1", "1.21.1");

        Assert.False(Directory.Exists(Scripts));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private Task RunAsync(string portableMinecraftVersion, string minecraftVersion)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Preparation scripts run in the Linux client image");

        return new GameRuntime().RunPreparationScriptsAsync(Scripts, "/game", portableMinecraftVersion, minecraftVersion, TestContext.Current.CancellationToken);
    }

    private void WriteScript(string name, string body)
    {
        string path = Path.Join(Directory.CreateDirectory(Scripts).FullName, name);
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
