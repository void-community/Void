using System.Net;
using System.Text;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class CurseForgeDownloadTests : IDisposable
{
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private readonly string _directory = Directory.CreateTempSubdirectory(prefix: "void-curseforge-").FullName;

    private string DestinationPath => Path.Combine(_directory, "tombstone-neoforge-1.21.1-9.5.6.jar");

    [Fact]
    public void ListsCdnAndWebsiteWhenApiHasNoDownloadUrl()
    {
        var candidates = GameRuntime.CreateCurseForgeDownloadUris(modId: 243707, fileId: 8842741, fileName: "tombstone-neoforge-1.21.1-9.5.6.jar", downloadUrl: null);

        Assert.Equal(
            [
                "https://edge.forgecdn.net/files/8842/741/tombstone-neoforge-1.21.1-9.5.6.jar",
                "https://www.curseforge.com/api/v1/mods/243707/files/8842741/download"
            ],
            candidates.Select(candidate => candidate.AbsoluteUri)
        );
    }

    [Fact]
    public void PrefersApiDownloadUrl()
    {
        var candidates = GameRuntime.CreateCurseForgeDownloadUris(modId: 1, fileId: 7000123, fileName: "mod.jar", downloadUrl: "https://edge.forgecdn.net/files/7000/123/mod [1.21].jar");

        Assert.Equal("https://edge.forgecdn.net/files/7000/123/mod%20%5B1.21%5D.jar", candidates[0].AbsoluteUri);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public void SkipsCdnPathForShortFileIds()
    {
        var candidates = GameRuntime.CreateCurseForgeDownloadUris(modId: 1, fileId: 123456, fileName: "legacy.jar", downloadUrl: null);

        Assert.Equal(["https://www.curseforge.com/api/v1/mods/1/files/123456/download"], candidates.Select(candidate => candidate.AbsoluteUri));
    }

    [Fact]
    public void EscapesCdnFileNames()
    {
        var candidates = GameRuntime.CreateCurseForgeDownloadUris(modId: 1, fileId: 8273779, fileName: "fabric api+1.21 [beta].jar", downloadUrl: null);

        Assert.Equal("https://edge.forgecdn.net/files/8273/779/fabric%20api%2B1.21%20%5Bbeta%5D.jar", candidates[0].AbsoluteUri);
    }

    [Fact]
    public void ReadsSha1FromPublishedHashes()
    {
        GameRuntime.CurseForgeFileHash[] hashes = [new(Value: "md5value", Algo: 2), new(Value: AbcSha1, Algo: 1)];

        Assert.Equal(AbcSha1, GameRuntime.GetCurseForgeSha1(hashes));
        Assert.Null(GameRuntime.GetCurseForgeSha1(hashes: null));
    }

    [Theory]
    [InlineData(AbcSha1, true)]
    [InlineData("A9993E364706816ABA3E25717850C26C9CD0D89D", true)]
    [InlineData("0000000000000000000000000000000000000000", false)]
    [InlineData(null, true)]
    public async Task ChecksContentAgainstSha1(string? expectedSha1, bool expectedMatch)
    {
        using var source = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        string actualSha1 = await GameRuntime.CopyWithSha1Async(source, Stream.Null, TestContext.Current.CancellationToken);

        Assert.Equal(expectedMatch, GameRuntime.IsExpectedSha1(actualSha1, expectedSha1));
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("", 4)]
    [InlineData("1", 1)]
    [InlineData(" 8 ", 8)]
    public void ReadsParallelDownloads(string? configured, int expected)
    {
        Assert.Equal(expected, GameRuntime.GetCurseForgeParallelDownloads(configured));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("four")]
    [InlineData("1.5")]
    public void RejectsParallelDownloadsThatAreNotPositiveIntegers(string configured)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameRuntime.GetCurseForgeParallelDownloads(configured));

        Assert.Equal("CURSEFORGE_PARALLEL_DOWNLOADS must be a positive integer", exception.Message);
    }

    [Fact]
    public async Task FallsBackWhenCdnRefusesFile()
    {
        StubHandler handler = new(request => request.RequestUri?.Host == "edge.forgecdn.net" ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Content("abc"));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal("abc", await File.ReadAllTextAsync(DestinationPath, TestContext.Current.CancellationToken));
        Assert.Equal(["edge.forgecdn.net returned 403"], result.Failures);
        Assert.Equal([DestinationPath], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SkipsContentWithWrongHash()
    {
        StubHandler handler = new(request => Content(request.RequestUri?.Host == "edge.forgecdn.net" ? "tampered" : "abc"));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal("abc", await File.ReadAllTextAsync(DestinationPath, TestContext.Current.CancellationToken));
        Assert.Contains("does not match SHA-1", result.Failures.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsEveryFailedCandidateAndLeavesNothingBehind()
    {
        StubHandler handler = new(request => request.RequestUri?.Host == "edge.forgecdn.net" ? Content("tampered") : new HttpResponseMessage(HttpStatusCode.NotFound));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.False(result.Downloaded);
        Assert.Equal(2, result.Failures.Count);
        Assert.Equal("www.curseforge.com returned 404", result.Failures[1]);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SendsApiKeyOnlyToCurseForge()
    {
        StubHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal([("edge.forgecdn.net", false), ("www.curseforge.com", true)], handler.Requests);
        Assert.False(result.Downloaded);
    }

    [Fact]
    public async Task RetriesOnlyCandidatesThatFailedTransiently()
    {
        // Issue #1613: every candidate was tried once, so a short CDN outage failed the download within milliseconds.
        int cdnRequests = 0;
        StubHandler handler = new(request => request.RequestUri?.Host == "edge.forgecdn.net"
            ? ++cdnRequests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Content("abc")
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal(["edge.forgecdn.net", "www.curseforge.com", "edge.forgecdn.net"], handler.Requests.Select(static request => request.Host));
        Assert.Equal("abc", await File.ReadAllTextAsync(DestinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OtherContentIsNotDownloadedAgain()
    {
        StubHandler handler = new(request => request.RequestUri?.Host == "edge.forgecdn.net" ? Content("tampered") : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.False(result.Downloaded);
        Assert.True(result.Transient);
        Assert.Equal(["edge.forgecdn.net", "www.curseforge.com", "www.curseforge.com", "www.curseforge.com"], handler.Requests.Select(static request => request.Host));
        Assert.Equal(["edge.forgecdn.net served content that does not match SHA-1 " + AbcSha1, "www.curseforge.com returned 503"], result.Failures);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CancellingRemovesThePartialDownload()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stalledStream = new StalledStream(Encoding.ASCII.GetBytes("partial"));
        StubHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stalledStream) });
        using HttpClient httpClient = new(handler);

        var download = GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", DestinationPath, TimeSpan.Zero, cancellation.Token);
        await stalledStream.Stalled.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Single(Directory.GetFiles(_directory));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static IReadOnlyList<Uri> Candidates()
    {
        return GameRuntime.CreateCurseForgeDownloadUris(modId: 243707, fileId: 8842741, fileName: "tombstone-neoforge-1.21.1-9.5.6.jar", downloadUrl: null);
    }

    private static HttpResponseMessage Content(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Host, bool HasApiKey)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri?.Host ?? "", request.Headers.Contains(name: "x-api-key")));

            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Serves a first chunk, then waits until the download is cancelled, like a connection that stops mid-file.</summary>
    private sealed class StalledStream(byte[] firstChunk) : MemoryStream(firstChunk)
    {
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Stalled => _stalled.Task;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);

            if (read > 0)
                return read;

            _stalled.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }
    }
}
