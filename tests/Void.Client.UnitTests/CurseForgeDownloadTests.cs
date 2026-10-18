using System.Net;
using System.Text;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class CurseForgeDownloadTests
{
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";

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
    public void ChecksContentAgainstSha1(string? expectedSha1, bool expectedMatch)
    {
        Assert.Equal(expectedMatch, GameRuntime.IsExpectedSha1(Encoding.ASCII.GetBytes("abc"), expectedSha1));
    }

    [Fact]
    public async Task FallsBackWhenCdnRefusesFile()
    {
        StubHandler handler = new(request => request.RequestUri?.Host == "edge.forgecdn.net" ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Content("abc"));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", TestContext.Current.CancellationToken);

        Assert.Equal("abc", Encoding.ASCII.GetString(result.Content ?? []));
        Assert.Equal(["edge.forgecdn.net returned 403"], result.Failures);
    }

    [Fact]
    public async Task SkipsContentWithWrongHash()
    {
        StubHandler handler = new(request => Content(request.RequestUri?.Host == "edge.forgecdn.net" ? "tampered" : "abc"));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", TestContext.Current.CancellationToken);

        Assert.Equal("abc", Encoding.ASCII.GetString(result.Content ?? []));
        Assert.Contains("does not match SHA-1", result.Failures.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsEveryFailedCandidate()
    {
        StubHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.NotFound));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", TestContext.Current.CancellationToken);

        Assert.Null(result.Content);
        Assert.Equal(["edge.forgecdn.net returned 404", "www.curseforge.com returned 404"], result.Failures);
    }

    [Fact]
    public async Task SendsApiKeyOnlyToCurseForge()
    {
        StubHandler handler = new(request => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using HttpClient httpClient = new(handler);

        var result = await GameRuntime.DownloadCurseForgeFileAsync(httpClient, Candidates(), AbcSha1, apiKey: "key", TestContext.Current.CancellationToken);

        Assert.Equal([("edge.forgecdn.net", false), ("www.curseforge.com", true)], handler.Requests);
        Assert.Null(result.Content);
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
}
