using System.Net;
using System.Net.Sockets;
using System.Text;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class UpstreamFailureTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory().FullName;

    public static TheoryData<Exception, bool> Failures => new()
    {
        { new HttpRequestException("Connection refused"), true },
        { new HttpRequestException("Service Unavailable", inner: null, HttpStatusCode.ServiceUnavailable), true },
        { new HttpRequestException("Too Many Requests", inner: null, HttpStatusCode.TooManyRequests), true },
        { new HttpRequestException("Request Timeout", inner: null, HttpStatusCode.RequestTimeout), true },
        { new HttpRequestException("Not Found", inner: null, HttpStatusCode.NotFound), false },
        { new HttpRequestException("Forbidden", inner: null, HttpStatusCode.Forbidden), false },
        { new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely"), true },
        { new IOException("Unable to read data from the transport connection", new SocketException((int)SocketError.ConnectionReset)), true },
        { new IOException("No space left on device"), false },
        { new TaskCanceledException("The request timed out"), true },
        { new InvalidOperationException("Minecraft 99.99.99 is not listed in the Mojang version manifest"), false }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void UnavailableUpstreamsAreTheOnesWorthTryingAgain(Exception exception, bool unavailable)
    {
        Assert.Equal(unavailable, GameRuntime.IsUpstreamUnavailable(exception, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CallerCancellationIsNotAnUnavailableUpstream()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.False(GameRuntime.IsUpstreamUnavailable(new TaskCanceledException(), cancellation.Token));
    }

    [Fact]
    public async Task DownloadsAreRetriedWhileTheUpstreamIsUnavailable()
    {
        int requests = 0;
        using HttpClient httpClient = new(new StubHandler(_ => ++requests == 1 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Content("jre")));
        string destination = Path.Join(_directory, "zulu.tar.gz");

        await GameRuntime.DownloadFileAsync(httpClient, new Uri("https://cdn.azul.test/zulu.tar.gz"), destination, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(2, requests);
        Assert.Equal("jre", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadsThatCannotSucceedFailAtOnce()
    {
        int requests = 0;
        using HttpClient httpClient = new(new StubHandler(_ =>
        {
            requests++;

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            GameRuntime.DownloadFileAsync(httpClient, new Uri("https://cdn.azul.test/missing.tar.gz"), Path.Join(_directory, "missing.tar.gz"), TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal(1, requests);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static HttpResponseMessage Content(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
