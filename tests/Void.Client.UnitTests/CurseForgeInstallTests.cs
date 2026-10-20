using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class CurseForgeInstallTests : IDisposable
{
    private static readonly Uri ApiBaseUri = new("https://api.curseforge.test/");
    private static readonly ModpackFile Shared = new(3001, "shared-1.0.jar", "shared");
    private static readonly ModpackFile OnlyInA = new(3002, "only-a-1.0.jar", "only a");
    private static readonly ModpackFile Textures = new(3003, "a-textures.zip", "textures");
    private static readonly ModpackFile Overridden = new(3004, "overridden-1.0.jar", "published");
    private static readonly ModpackFile OnlyInB = new(3005, "only-b-1.0.jar", "only b");
    private static readonly ModpackFile Broken = new(3006, "broken-1.0.jar", "broken");

    private static readonly Modpack A = new(
        "pack-a", 1001, 2001, [Shared, OnlyInA, Textures, Overridden],
        new Dictionary<string, string>
        {
            ["config/a.toml"] = "a = true",
            ["kubejs/server_scripts/a.js"] = "// a",
            ["mods/overridden-1.0.jar"] = "override",
            ["options.txt"] = "renderDistance:8"
        }
    );

    private static readonly Modpack B = new(
        "pack-b", 1002, 2002, [Shared, OnlyInB, Broken],
        new Dictionary<string, string> { ["config/b.toml"] = "b = true", ["kubejs/server_scripts/b.js"] = "// b" }
    );

    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public async Task FailedInstallDoesNotPassAsThePreviousModpack()
    {
        // Issue #1615: the previous modpack's marker survived a failed install, so that modpack then started with the failed one's mods.
        FakeCurseForge curseForge = new([A, B]);
        await PrepareAsync(curseForge, A);
        await File.WriteAllTextAsync(Path.Join(_directory, "options.txt"), "renderDistance:2", TestContext.Current.CancellationToken);
        curseForge.Failing.Add(Broken.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareAsync(curseForge, B));

        Assert.Contains("broken-1.0.jar", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Join(_directory, ".curseforge-modpack")));
        Assert.Equal(["only-b-1.0.jar", "shared-1.0.jar"], Files("mods"));
        // The previous modpack's scripts and configs are gone, while the consumer's options stay.
        Assert.False(File.Exists(Path.Join(_directory, "config", "a.toml")));
        Assert.False(File.Exists(Path.Join(_directory, "kubejs", "server_scripts", "a.js")));
        Assert.Equal("renderDistance:2", await File.ReadAllTextAsync(Path.Join(_directory, "options.txt"), TestContext.Current.CancellationToken));

        curseForge.Failing.Clear();
        int searches = curseForge.Searches;
        await PrepareAsync(curseForge, A);

        Assert.Equal(searches + 1, curseForge.Searches);
        Assert.Equal(["only-a-1.0.jar", "overridden-1.0.jar", "shared-1.0.jar"], Files("mods"));
        Assert.Equal(["a-textures.zip"], Files("resourcepacks"));
        Assert.Equal("override", await File.ReadAllTextAsync(Path.Join(_directory, "mods", Overridden.FileName), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Join(_directory, "config", "a.toml")));
        Assert.False(File.Exists(Path.Join(_directory, "config", "b.toml")));
        Assert.Equal(1, curseForge.Downloads[Shared.FileName]);
    }

    [Fact]
    public async Task RetryKeepsWhatTheFailedInstallDownloaded()
    {
        FakeCurseForge curseForge = new([A, B]);
        await PrepareAsync(curseForge, A);
        curseForge.Failing.Add(Broken.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareAsync(curseForge, B));
        curseForge.Failing.Clear();

        await PrepareAsync(curseForge, B);

        Assert.Equal("pack-b 2002", await File.ReadAllTextAsync(Path.Join(_directory, ".curseforge-modpack"), TestContext.Current.CancellationToken));
        Assert.Equal(["broken-1.0.jar", "only-b-1.0.jar", "shared-1.0.jar"], Files("mods"));
        Assert.Empty(Files("resourcepacks"));
        Assert.Equal(1, curseForge.Downloads[Shared.FileName]);
        Assert.Equal(1, curseForge.Downloads[OnlyInB.FileName]);
        Assert.True(File.Exists(Path.Join(_directory, "config", "b.toml")));
    }

    [Fact]
    public async Task StoppedInstallDoesNotPassAsThePreviousModpack()
    {
        FakeCurseForge curseForge = new([A, B]);
        await PrepareAsync(curseForge, B);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        curseForge.Stalling = OnlyInA.Id;

        var install = PrepareAsync(curseForge, A, stop.Token);
        await curseForge.Stalled.Task.WaitAsync(TestContext.Current.CancellationToken);
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        Assert.False(File.Exists(Path.Join(_directory, ".curseforge-modpack")));
        Assert.DoesNotContain(OnlyInB.FileName, Files("mods"));

        curseForge.Stalling = null;
        await PrepareAsync(curseForge, B);

        Assert.Equal(["broken-1.0.jar", "only-b-1.0.jar", "shared-1.0.jar"], Files("mods"));
        Assert.Equal(1, curseForge.Downloads[Shared.FileName]);
    }

    [Fact]
    public async Task InstalledModpackStartsWithoutTheApi()
    {
        FakeCurseForge curseForge = new([A]);
        string version = await PrepareAsync(curseForge, A);
        curseForge.Failing.Add(0);

        Assert.Equal(version, await PrepareAsync(curseForge, A));
        Assert.Equal(1, curseForge.Searches);
    }

    [Fact]
    public async Task FileIsReusedOnlyWithItsPublishedSha1()
    {
        string path = Path.Join(_directory, "mod.jar");
        await File.WriteAllTextAsync(path, "abc", TestContext.Current.CancellationToken);

        Assert.True(await GameRuntime.IsInstalledFileAsync(path, "a9993e364706816aba3e25717850c26c9cd0d89d", TestContext.Current.CancellationToken));
        Assert.False(await GameRuntime.IsInstalledFileAsync(path, "0000000000000000000000000000000000000000", TestContext.Current.CancellationToken));
        Assert.False(await GameRuntime.IsInstalledFileAsync(path, expectedSha1: null, TestContext.Current.CancellationToken));
        Assert.False(await GameRuntime.IsInstalledFileAsync(Path.Join(_directory, "missing.jar"), "a9993e364706816aba3e25717850c26c9cd0d89d", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PruningKeepsOnlyTheModpackFiles()
    {
        string mods = Directory.CreateDirectory(Path.Join(_directory, "mods")).FullName;
        File.WriteAllText(Path.Join(mods, "kept.jar"), "kept");
        File.WriteAllText(Path.Join(mods, "other.jar"), "other");
        File.WriteAllText(Path.Join(mods, "kept.jar.0123.download"), "partial");
        Directory.CreateDirectory(Path.Join(mods, "1.21.1"));
        File.WriteAllText(Path.Join(mods, "1.21.1", "nested.jar"), "nested");
        string elsewhere = Directory.CreateDirectory(Path.Join(_directory, "elsewhere")).FullName;
        File.WriteAllText(Path.Join(elsewhere, "keep.txt"), "keep");
        Directory.CreateSymbolicLink(Path.Join(mods, "linked"), elsewhere);

        GameRuntime.PruneModpackFiles(_directory, new HashSet<string>(StringComparer.Ordinal) { Path.Join(mods, "kept.jar") });

        Assert.Equal(["kept.jar"], Files("mods"));
        Assert.Empty(Directory.GetDirectories(mods));
        Assert.True(File.Exists(Path.Join(elsewhere, "keep.txt")));
        Assert.True(Directory.Exists(Path.Join(_directory, "resourcepacks")));
        Assert.True(Directory.Exists(Path.Join(_directory, "shaderpacks")));
    }

    [Fact]
    public void OnlyStaleOverridesInSubdirectoriesAreRemoved()
    {
        string outside = Path.Join(Directory.GetParent(_directory)!.FullName, $"outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "outside");

        try
        {
            foreach (string file in new[] { "config/old.toml", "config/kept.toml", "options.txt" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Join(_directory, file))!);
                File.WriteAllText(Path.Join(_directory, file), file);
            }

            GameRuntime.RemoveStaleOverrides(_directory, ["config/old.toml", "config/kept.toml", "options.txt", $"config/../../{Path.GetFileName(outside)}"], ["config/kept.toml"]);

            Assert.False(File.Exists(Path.Join(_directory, "config", "old.toml")));
            Assert.True(File.Exists(Path.Join(_directory, "config", "kept.toml")));
            Assert.True(File.Exists(Path.Join(_directory, "options.txt")));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private Task<string> PrepareAsync(FakeCurseForge curseForge, Modpack modpack, CancellationToken? cancellationToken = null)
    {
        HttpClient httpClient = new(curseForge, disposeHandler: false);

        return PrepareAndDisposeAsync();

        async Task<string> PrepareAndDisposeAsync()
        {
            using (httpClient)
            {
                return await GameRuntime.PrepareCurseForgeAsync(
                    httpClient,
                    modpack.Slug,
                    modpack.FileId,
                    curseForgeApiKey: "key",
                    ApiBaseUri,
                    parallelDownloads: 2,
                    _directory,
                    cancellationToken ?? TestContext.Current.CancellationToken
                );
            }
        }
    }

    private string[] Files(string folder)
    {
        string directory = Path.Join(_directory, folder);

        return Directory.Exists(directory) ? [.. Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)!] : [];
    }

    private sealed record ModpackFile(int Id, string FileName, string Content)
    {
        public byte[] Bytes => Encoding.UTF8.GetBytes(Content);

        public string Sha1 => Convert.ToHexStringLower(SHA1.HashData(Bytes));
    }

    private sealed record Modpack(string Slug, int ProjectId, int FileId, IReadOnlyList<ModpackFile> Files, IReadOnlyDictionary<string, string> Overrides)
    {
        public ModpackFile Archive => new(FileId, $"{Slug}.zip", Convert.ToBase64String(CreateArchive()));

        private byte[] CreateArchive()
        {
            using MemoryStream buffer = new();

            using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = new
                {
                    minecraft = new { version = "1.21.1", modLoaders = new[] { new { id = "neoforge-21.1.230", primary = true } } },
                    overrides = "overrides",
                    files = Files.Select(file => new { projectID = file.Id + 10000, fileID = file.Id, required = true })
                };

                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write(JsonSerializer.Serialize(manifest));

                foreach (var (path, content) in Overrides)
                {
                    using var writer = new StreamWriter(archive.CreateEntry($"overrides/{path}").Open());
                    writer.Write(content);
                }
            }

            return buffer.ToArray();
        }
    }

    /// <summary>Serves the CurseForge API and downloads for the given modpacks, failing or stalling the files a test chooses.</summary>
    private sealed class FakeCurseForge(IReadOnlyList<Modpack> modpacks) : HttpMessageHandler
    {
        private readonly Dictionary<int, ModpackFile> _files = modpacks.SelectMany(static modpack => modpack.Files).DistinctBy(static file => file.Id).ToDictionary(static file => file.Id);
        private readonly Dictionary<int, byte[]> _archives = modpacks.ToDictionary(static modpack => modpack.FileId, static modpack => Convert.FromBase64String(modpack.Archive.Content));
        private int _searches;

        public ConcurrentDictionary<string, int> Downloads { get; } = new(StringComparer.Ordinal);

        /// <summary>File ids whose downloads fail, or 0 to fail every API request.</summary>
        public HashSet<int> Failing { get; } = [];

        public int Searches => _searches;

        public int? Stalling { get; set; }

        public TaskCompletionSource Stalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;

            if (uri.Host == ApiBaseUri.Host)
                return Failing.Contains(0) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : await ApiAsync(request, uri, cancellationToken);

            if (uri.Host != "files.curseforge.test")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            int id = int.Parse(uri.Segments[1].TrimEnd('/'), System.Globalization.CultureInfo.InvariantCulture);

            if (_archives.TryGetValue(id, out byte[]? archive))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };

            if (Failing.Contains(id))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            if (Stalling == id)
            {
                Stalled.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            ModpackFile file = _files[id];
            Downloads.AddOrUpdate(file.FileName, 1, static (_, count) => count + 1);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Bytes) };
        }

        private async Task<HttpResponseMessage> ApiAsync(HttpRequestMessage request, Uri uri, CancellationToken cancellationToken)
        {
            if (uri.AbsolutePath == "/v1/mods/search")
            {
                Interlocked.Increment(ref _searches);
                string slug = uri.Query.Split("slug=")[1];
                Modpack modpack = modpacks.Single(modpack => modpack.Slug == slug);

                return Json(new[] { new { id = modpack.ProjectId, slug } });
            }

            if (uri.AbsolutePath == "/v1/mods/files")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                int[] ids = [.. body.RootElement.GetProperty("fileIds").EnumerateArray().Select(static id => id.GetInt32())];

                return Json(ids.Select(id => Describe(_files[id], id + 10000)));
            }

            Modpack archiveModpack = modpacks.Single(modpack => uri.AbsolutePath == $"/v1/mods/{modpack.ProjectId}/files/{modpack.FileId}");
            ModpackFile archiveFile = new(archiveModpack.FileId, $"{archiveModpack.Slug}.zip", "");

            return Json(Describe(archiveFile, archiveModpack.ProjectId, _archives[archiveModpack.FileId]));
        }

        private static object Describe(ModpackFile file, int modId, byte[]? bytes = null)
        {
            string sha1 = bytes is null ? file.Sha1 : Convert.ToHexStringLower(SHA1.HashData(bytes));

            return new { id = file.Id, modId, fileName = file.FileName, downloadUrl = $"https://files.curseforge.test/{file.Id}/{file.FileName}", hashes = new[] { new { value = sha1, algo = 1 } } };
        }

        private static HttpResponseMessage Json(object data)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data }), Encoding.UTF8, "application/json") };
        }
    }
}
