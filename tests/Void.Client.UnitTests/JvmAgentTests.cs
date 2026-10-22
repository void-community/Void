using System.Net;
using System.Text;
using Void.Client;
using Void.Client.Failures;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class JvmAgentTests : IDisposable
{
    private const string AgentUrl = "https://github.test/agent/releases/latest/download/agent-linux-x64.so";
    private const string JavaAgentUrl = "https://github.test/agent/releases/latest/download/agent.jar";

    private readonly string _directory = Directory.CreateTempSubdirectory(prefix: "void-agents-").FullName;

    private string CacheDirectory => Path.Join(_directory, "cache");

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("/opt/first.so", new[] { "/opt/first.so" })]
    [InlineData("/opt/first.so,https://github.test/second.so", new[] { "/opt/first.so", "https://github.test/second.so" })]
    [InlineData(" /opt/first.so , /opt/second.so ", new[] { "/opt/first.so", "/opt/second.so" })]
    [InlineData("/opt/first.so\r\n\n/opt/second.so\n", new[] { "/opt/first.so", "/opt/second.so" })]
    [InlineData("# Agents\n/opt/first.so # patches Forge\n,,", new[] { "/opt/first.so" })]
    [InlineData("/opt/first agent.so", new[] { "/opt/first agent.so" })]
    public void ListsSplitAsInItzgImages(string? value, string[] expected)
    {
        Assert.Equal(expected, GameRuntime.SplitList(value));
    }

    [Fact]
    public async Task PathsLoadAsTheyAre()
    {
        string agent = Path.Join(_directory, "agent.so");
        await File.WriteAllTextAsync(agent, "agent", TestContext.Current.CancellationToken);
        StubHandler handler = new(_ => Content("unused"));
        using HttpClient httpClient = new(handler);

        Assert.Equal(agent, await ResolveAsync(httpClient, agent));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task UrlsDownloadOnce()
    {
        StubHandler handler = new(_ => Content("agent"));
        using HttpClient httpClient = new(handler);

        string first = await ResolveAsync(httpClient, AgentUrl);
        string second = await ResolveAsync(httpClient, AgentUrl);

        Assert.Equal(first, second);
        Assert.Equal("agent-linux-x64.so", Path.GetFileName(first));
        Assert.Equal("agent", await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task UrlsWithTheSameFileNameKeepTheirOwnAgents()
    {
        StubHandler handler = new(request => Content(request.RequestUri?.Host ?? ""));
        using HttpClient httpClient = new(handler);

        string first = await ResolveAsync(httpClient, "https://first.test/agent.so");
        string second = await ResolveAsync(httpClient, "https://second.test/agent.so");

        Assert.NotEqual(first, second);
        Assert.Equal("first.test", await File.ReadAllTextAsync(first, TestContext.Current.CancellationToken));
        Assert.Equal("second.test", await File.ReadAllTextAsync(second, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NativeAgentsAndJavaAgentsLoadWithTheirOwnOptions()
    {
        string nativeAgent = Path.Join(_directory, "agent.so");
        string javaAgent = Path.Join(_directory, "agent.jar");
        await File.WriteAllTextAsync(nativeAgent, "native", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(javaAgent, "java", TestContext.Current.CancellationToken);

        Dictionary<string, string> variables = new() { ["JVM_AGENTS"] = nativeAgent, ["JAVA_AGENTS"] = $"{javaAgent},{JavaAgentUrl}" };
        StubHandler handler = new(_ => Content("downloaded"));
        using HttpClient httpClient = new(handler);

        IReadOnlyList<string> arguments = await GameRuntime.CreateJvmAgentArgumentsAsync(httpClient, variables.GetValueOrDefault, CacheDirectory, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(3, arguments.Count);
        Assert.Equal($"--jvm-arg=-agentpath:{nativeAgent}", arguments[0]);
        Assert.Equal($"--jvm-arg=-javaagent:{javaAgent}", arguments[1]);
        Assert.StartsWith($"--jvm-arg=-javaagent:{CacheDirectory}", arguments[2]);
    }

    [Fact]
    public async Task MissingPathsFailTheStart()
    {
        StubHandler handler = new(_ => Content("unused"));
        using HttpClient httpClient = new(handler);

        var exception = await Assert.ThrowsAsync<GameClientException>(() => ResolveAsync(httpClient, Path.Join(_directory, "missing.so")));

        Assert.Equal("client.install.failed", exception.Failure.Code);
        Assert.Equal("agent", exception.Failure.Stage);
    }

    [Theory]
    [InlineData("agent.so")]
    [InlineData("ftp://github.test/agent.so")]
    public async Task OtherAgentsFailTheStart(string agent)
    {
        StubHandler handler = new(_ => Content("unused"));
        using HttpClient httpClient = new(handler);

        var exception = await Assert.ThrowsAsync<GameClientException>(() => ResolveAsync(httpClient, agent));

        Assert.Equal("client.install.failed", exception.Failure.Code);
        Assert.Equal("agent", exception.Failure.Stage);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "client.install.failed")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "client.install.unavailable")]
    public async Task FailedDownloadsFailTheStart(HttpStatusCode status, string code)
    {
        StubHandler handler = new(_ => new HttpResponseMessage(status));
        using HttpClient httpClient = new(handler);

        var exception = await Assert.ThrowsAsync<GameClientException>(() => ResolveAsync(httpClient, AgentUrl));

        Assert.Equal(code, exception.Failure.Code);
        Assert.Equal("agent", exception.Failure.Stage);
        Assert.False(Directory.Exists(CacheDirectory) && Directory.EnumerateFiles(CacheDirectory, searchPattern: "*", SearchOption.AllDirectories).Any());
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static HttpResponseMessage Content(string text)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(text)) };
    }

    private Task<string> ResolveAsync(HttpClient httpClient, string agent)
    {
        return GameRuntime.ResolveJvmAgentAsync(httpClient, agent, CacheDirectory, TimeSpan.Zero, TestContext.Current.CancellationToken);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;

            return Task.FromResult(respond(request));
        }
    }
}
