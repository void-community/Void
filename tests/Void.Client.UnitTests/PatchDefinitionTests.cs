using Void.Client;
using Void.Client.Failures;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class PatchDefinitionTests : IDisposable
{
    private const string FmlConfig = "# FML's own config\ndisableConfigWatcher = false\nmaxThreads = -1\n\n[earlyWindow]\nwidth = 854\n";
    private const string FmlPatch = """{"file":"config/fml.toml","ops":[{"$set":{"path":"$.disableConfigWatcher","value":"${CFG_FML_DISABLE_CONFIG_WATCHER}","value-type":"bool"}}]}""";

    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    private string Game => Path.Join(_directory, "game");

    [Fact]
    public async Task SetsTomlValueFromVariable()
    {
        // Issue #1630: All the Mods 10 freezes unless FML's config watcher is off, which servers on itzg's image patch in config/fml.toml.
        WriteFile("game/config/fml.toml", FmlConfig);
        string definitions = WriteFile("fml.patch.json", FmlPatch);

        List<string> messages = await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions, ["CFG_FML_DISABLE_CONFIG_WATCHER"] = "TRUE" });

        string patched = ReadFile("game/config/fml.toml");
        Assert.Contains("disableConfigWatcher = true", patched, StringComparison.Ordinal);
        Assert.Contains("maxThreads = -1", patched, StringComparison.Ordinal);
        Assert.Contains("width = 854", patched, StringComparison.Ordinal);
        Assert.StartsWith("Applied $set $.disableConfigWatcher", Assert.Single(messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchesTheModpackOfCurseForgeStart()
    {
        // A CurseForge start runs the game in the modpack directory, so a relative file resolves there.
        WriteFile("game/config/fml.toml", FmlConfig);
        WriteFile("game/modpack/config/fml.toml", FmlConfig);
        string definitions = WriteFile("fml.patch.json", FmlPatch);

        await ApplyAsync(Path.Join(Game, "modpack"), new() { ["VOID_PATCH_DEFINITIONS"] = definitions, ["CFG_FML_DISABLE_CONFIG_WATCHER"] = "yes" });

        Assert.Contains("disableConfigWatcher = true", ReadFile("game/modpack/config/fml.toml"), StringComparison.Ordinal);
        Assert.Equal(FmlConfig, ReadFile("game/config/fml.toml"));
    }

    [Fact]
    public async Task MissingFileWarnsAndTheRestApply()
    {
        WriteFile("game/options.json", """{"a":1}""");
        string definitions = WriteFile("set.json", """
            {"patches":[
              {"file":"config/missing.toml","ops":[{"$set":{"path":"$.a","value":"1"}}]},
              {"file":"options.json","ops":[{"$set":{"path":"$.a","value":"2","value-type":"int"}}]}
            ]}
            """);

        List<string> messages = await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions });

        Assert.Equal($"Unable to patch {Path.Join(Game, "config/missing.toml")}: it isn't an existing file", messages[0]);
        Assert.Contains("\"a\": 2", ReadFile("game/options.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingVariableSkipsOnlyItsOperation()
    {
        WriteFile("game/options.json", """{"a":"old","b":"old"}""");
        string definitions = WriteFile("patch.json", """
            {"file":"options.json","ops":[
              {"$set":{"path":"$.a","value":"${CFG_MISSING}"}},
              {"$set":{"path":"$.b","value":"${CFG_PRESENT}-${OTHER}"}}
            ]}
            """);

        List<string> messages = await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions, ["CFG_PRESENT"] = "new", ["OTHER"] = "ignored" });

        string patched = ReadFile("game/options.json");
        Assert.Contains("\"a\": \"old\"", patched, StringComparison.Ordinal);
        Assert.Contains("\"b\": \"new-${OTHER}\"", patched, StringComparison.Ordinal);
        Assert.EndsWith("missing environment variables CFG_MISSING", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("Applied $set $.b", messages[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValueComesFromTheFileItsVariableNames()
    {
        WriteFile("game/options.json", "{}");
        string secret = WriteFile("secret", "  from-file \n");
        string definitions = WriteFile("patch.json", """{"file":"options.json","ops":[{"$put":{"path":"$","key":"token","value":"${CFG_TOKEN}"}}]}""");

        await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions, ["CFG_TOKEN_FILE"] = secret, ["CFG_TOKEN"] = "from-variable" });

        Assert.Contains("\"token\": \"from-file\"", ReadFile("game/options.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectorySourceAppliesItsDefinitionsInNameOrder()
    {
        WriteFile("game/options.json", """{"list":[]}""");
        WriteFile("patches/20-second.json", """{"file":"options.json","ops":[{"$add":{"path":"$.list","value":"second"}}]}""");
        WriteFile("patches/10-first.json", """{"file":"options.json","ops":[{"$add":{"path":"$.list","value":"1, 2","value-type":"list of int"}}]}""");
        WriteFile("patches/notes.txt", "not a patch");

        await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = Path.Join(_directory, "patches") });

        string patched = ReadFile("game/options.json").ReplaceLineEndings("").Replace(" ", "", StringComparison.Ordinal);
        Assert.Equal("""{"list":[[1,2],"second"]}""", patched);
    }

    [Fact]
    public async Task PatchesPropertiesKeepingTheirOrderAndEscapes()
    {
        WriteFile("game/server.properties", "# comment\nmotd=A\\:B\nmax-players = 20\nlevel-name=world\n");
        string definitions = WriteFile("patch.json", """
            {"file":"server.properties","ops":[
              {"$set":{"path":"$.max-players","value":"30"}},
              {"$set":{"path":"$['white list']","value":"true","value-type":"bool"}}
            ]}
            """);

        await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions });

        Assert.Equal("motd=A\\:B\nmax-players=30\nlevel-name=world\nwhite\\ list=true\n", ReadFile("game/server.properties"));
    }

    [Fact]
    public async Task UnsupportedFormatWarns()
    {
        WriteFile("game/config.yml", "a: 1\n");
        string definitions = WriteFile("patch.json", """{"file":"config.yml","ops":[{"$set":{"path":"$.a","value":"2"}}]}""");

        List<string> messages = await ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions });

        Assert.EndsWith("the file format yaml isn't supported", Assert.Single(messages), StringComparison.Ordinal);
        Assert.Equal("a: 1\n", ReadFile("game/config.yml"));
    }

    [Fact]
    public async Task OperationOnMissingPathFailsTheStart()
    {
        WriteFile("game/config/fml.toml", FmlConfig);
        string definitions = WriteFile("patch.json", """{"file":"config/fml.toml","ops":[{"$set":{"path":"$.missing.value","value":"1"}}]}""");

        var failure = await Assert.ThrowsAsync<GameClientException>(() => ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = definitions }));

        Assert.Equal("client.install.failed", failure.Failure.Code);
        Assert.Equal("minecraft", failure.Failure.Stage);
        Assert.Contains("$['missing'] doesn't exist", failure.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(FmlConfig, ReadFile("game/config/fml.toml"));
    }

    [Fact]
    public async Task SourceThatDoesNotExistFailsTheStart()
    {
        var failure = await Assert.ThrowsAsync<GameClientException>(() => ApplyAsync(Game, new() { ["VOID_PATCH_DEFINITIONS"] = Path.Join(_directory, "missing.json") }));

        Assert.Equal("client.install.failed", failure.Failure.Code);
        Assert.Equal("minecraft", failure.Failure.Stage);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static async Task<List<string>> ApplyAsync(string gameDirectory, Dictionary<string, string?> variables)
    {
        using HttpClient httpClient = new();

        return await GameRuntime.ApplyPatchDefinitionsAsync(httpClient, variables.GetValueOrDefault, gameDirectory, TestContext.Current.CancellationToken);
    }

    private string WriteFile(string relativePath, string content)
    {
        string path = Path.Join(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }

    private string ReadFile(string relativePath)
    {
        return File.ReadAllText(Path.Join(_directory, relativePath));
    }
}
