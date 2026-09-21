using System.IO.Compression;
using System.Collections;
using System.Reflection;
using Nito.AsyncEx;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public class SessionDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"void-diagnostics-tests-{Guid.NewGuid()}");

    private SessionDiagnostics Create(int maximumSessions = 10, int maximumSessionMb = 32, int maximumTotalMb = 256) => new(new DiagnosticsOptions
    {
        Directory = Path.Combine(_directory, "evidence"),
        MaximumSessions = maximumSessions,
        MaximumSessionMb = maximumSessionMb,
        MaximumTotalMb = maximumTotalMb
    });

    [Fact]
    public void ConfigurationUsesPrefixedUppercaseNames()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VOID_DIAGNOSTICS_DIRECTORY"] = _directory,
            ["VOID_DIAGNOSTICS_MAXIMUM_SESSIONS"] = "5",
            ["VOID_DIAGNOSTICS_MAXIMUM_TOTAL_MB"] = "128",
            ["VOID_DIAGNOSTICS_MAXIMUM_SESSION_MB"] = "16"
        }).Build();

        var options = DiagnosticsOptions.FromConfiguration(configuration);

        Assert.Equal(_directory, options.Directory);
        Assert.Equal(5, options.MaximumSessions);
        Assert.Equal(128, options.MaximumTotalMb);
        Assert.Equal(16, options.MaximumSessionMb);
    }

    [Fact]
    public void MissingConfigurationPreservesRetentionDefaults()
    {
        var options = DiagnosticsOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal("/var/lib/void-client/diagnostics", options.Directory);
        Assert.Equal(10, options.MaximumSessions);
        Assert.Equal(256, options.MaximumTotalMb);
        Assert.Equal(32, options.MaximumSessionMb);
    }

    [Fact]
    public async Task ReportsAndFailureSurviveStopAndAnotherLaunchWithoutMixingSessions()
    {
        var gameDirectory = Path.Combine(_directory, "minecraft");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "debug"));
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "debug", "disconnect-old.txt"), "old report", TestContext.Current.CancellationToken);
        var diagnostics = Create();
        var first = await diagnostics.BeginAsync("vanilla:1.21", gameDirectory, TestContext.Current.CancellationToken);
        var failure = new GameClientException("client.connect.rejected", "connect", "connection.rejected", "bad packet").Failure;
        await diagnostics.RecordAsync(new(GameState.Ready, 2, "connect", OperationState.Failed, 1, null, null, "failed", failure.Message, failure, [], DateTimeOffset.UtcNow, first), TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(first, "stderr", "first output", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "debug", "disconnect-new.txt"), "original report", TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(first, TestContext.Current.CancellationToken);
        await diagnostics.RecordAsync(new(GameState.Idle, 3, "stop", OperationState.Succeeded, null, 0, null, "stopped", null, null, [], DateTimeOffset.UtcNow, first), TestContext.Current.CancellationToken);
        var second = await diagnostics.BeginAsync("vanilla:1.22", gameDirectory, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "debug", "disconnect-new.txt"), "overwritten report", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(second, "stdout", "second output", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(first, "stderr", " late first output", TestContext.Current.CancellationToken);

        var firstFiles = await ReadArchiveAsync(diagnostics, first);
        Assert.Equal("original report", firstFiles["debug-disconnect-new.txt"]);
        Assert.DoesNotContain("debug-disconnect-old.txt", firstFiles.Keys);
        Assert.Equal("first output late first output", firstFiles["console-stderr.log"]);
        Assert.Contains("client.connect.rejected", firstFiles["session.json"]);
        Assert.Contains("\"operation\":\"connect\"", firstFiles["operations.jsonl"]);
        Assert.Contains("\"operation\":\"stop\"", firstFiles["operations.jsonl"]);
        var secondFiles = await ReadArchiveAsync(diagnostics, second);
        Assert.DoesNotContain("console-stderr.log", secondFiles.Keys);
        Assert.Equal("second output", secondFiles["console-stdout.log"]);
    }

    [Fact]
    public async Task RetentionExpiresOldestCompletedSessionAndReloadsStoredEvidence()
    {
        var diagnostics = Create(maximumSessions: 1);
        var first = await diagnostics.BeginAsync("first", "", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(first, "stdout", "old", TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(first, TestContext.Current.CancellationToken);
        var second = await diagnostics.BeginAsync("second", "", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(second, "stdout", "new", TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(second, TestContext.Current.CancellationToken);
        Assert.Null(await diagnostics.DownloadAsync(first, CancellationToken.None));
        Assert.Equal(second, Assert.Single((await diagnostics.ListAsync(TestContext.Current.CancellationToken))).SessionId);
        var reloaded = Create(maximumSessions: 1);
        Assert.Equal("new", (await ReadArchiveAsync(reloaded, second))["console-stdout.log"]);
    }

    [Fact]
    public async Task StorageFailureStillReturnsManifestWithCollectionWarning()
    {
        Directory.CreateDirectory(_directory);
        var blockedDirectory = Path.Combine(_directory, "file");
        await File.WriteAllTextAsync(blockedDirectory, "not a directory", TestContext.Current.CancellationToken);
        var diagnostics = new SessionDiagnostics(new DiagnosticsOptions { Directory = blockedDirectory });
        var sessionId = await diagnostics.BeginAsync("failed preparation", "", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(sessionId, "stderr", "failure", TestContext.Current.CancellationToken);
        var files = await ReadArchiveAsync(diagnostics, sessionId);
        Assert.Contains("Could not store", files["session.json"]);
    }

    [Fact]
    public async Task OversizedOutputIsBoundedAndDescribedInManifest()
    {
        var diagnostics = Create(maximumSessionMb: 1, maximumTotalMb: 1);
        var sessionId = await diagnostics.BeginAsync("noisy client", "", TestContext.Current.CancellationToken);
        for (var index = 0; index < 32; index++)
            await diagnostics.WriteOutputAsync(sessionId, "stderr", new string('x', 65536), TestContext.Current.CancellationToken);
        var files = await ReadArchiveAsync(diagnostics, sessionId);
        Assert.Contains("size limit reached", files["session.json"]);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_directory, "evidence"), "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) <= 1024 * 1024);
    }

    [Fact]
    public async Task TokensAndSymlinkedReportsAreExcluded()
    {
        var gameDirectory = Path.Combine(_directory, "minecraft");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "debug"));
        var diagnostics = Create();
        var id = await diagnostics.BeginAsync("test", gameDirectory, TestContext.Current.CancellationToken);
        using (diagnostics.Enter(id))
            await diagnostics.RegisterSecretAsync("private-agent-token", TestContext.Current.CancellationToken);
        await diagnostics.WriteOutputAsync(id, "stdout", "token=private-agent-token", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(gameDirectory, "debug", "disconnect-real.txt"), "private-agent-token", TestContext.Current.CancellationToken);
        var secretFile = Path.Combine(_directory, "credentials.txt");
        await File.WriteAllTextAsync(secretFile, "private credentials", TestContext.Current.CancellationToken);
        if (!OperatingSystem.IsWindows())
            File.CreateSymbolicLink(Path.Combine(gameDirectory, "debug", "disconnect-link.txt"), secretFile);
        var files = await ReadArchiveAsync(diagnostics, id);
        Assert.DoesNotContain("debug-disconnect-link.txt", files.Keys);
        Assert.All(files.Values, value => Assert.DoesNotContain("private-agent-token", value));
        Assert.Equal("[redacted]", files["debug-disconnect-real.txt"]);
    }

    [Fact]
    public async Task ParallelDownloadsAndOutputRemainReadable()
    {
        var diagnostics = Create();
        var id = await diagnostics.BeginAsync("test", "", TestContext.Current.CancellationToken);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            await diagnostics.WriteOutputAsync(id, "stdout", $"line {index}\n", TestContext.Current.CancellationToken);
            var files = await ReadArchiveAsync(diagnostics, id);
            using var manifest = JsonDocument.Parse(files["session.json"]);
            Assert.Equal(id, manifest.RootElement.GetProperty("sessionId").GetGuid());
        }));
    }

    [Fact]
    public async Task OutputPumpRedactsTokensAcrossReadsAndPreservesConsoleOutput()
    {
        var diagnostics = Create();
        var id = await diagnostics.BeginAsync("test", "", TestContext.Current.CancellationToken);
        var token = new string('a', 64);
        var encodedToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        using (diagnostics.Enter(id))
        {
            await diagnostics.RegisterSecretAsync(token, TestContext.Current.CancellationToken);
            await diagnostics.RegisterSecretAsync(encodedToken, TestContext.Current.CancellationToken);
        }
        var text = new string('x', 4000) + token + " " + encodedToken + " end";
        using var reader = new ChunkedReader(text);
        using var console = new StringWriter();
        var runtime = new GameRuntime(diagnostics);
        await runtime.PumpOutputAsync(reader, console, id, "stderr", TestContext.Current.CancellationToken);
        Assert.Equal(text, console.ToString());
        var retained = (await ReadArchiveAsync(diagnostics, id))["console-stderr.log"];
        Assert.DoesNotContain(token, retained);
        Assert.DoesNotContain(encodedToken, retained);
        Assert.EndsWith("[redacted] [redacted] end", retained);
    }

    [Fact]
    public async Task LateCollectionCannotOverwriteCompletedSessionReports()
    {
        var gameDirectory = Path.Combine(_directory, "minecraft");
        Directory.CreateDirectory(Path.Combine(gameDirectory, "debug"));
        var report = Path.Combine(gameDirectory, "debug", "disconnect-test.txt");
        var diagnostics = Create();
        var first = await diagnostics.BeginAsync("first", gameDirectory, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(report, "first report", TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(first, TestContext.Current.CancellationToken);
        var endedAt = (await diagnostics.ListAsync(TestContext.Current.CancellationToken))[0].EndedAt;
        await diagnostics.BeginAsync("second", gameDirectory, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(report, "second report", TestContext.Current.CancellationToken);
        await diagnostics.CollectAsync(first, TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(first, TestContext.Current.CancellationToken);
        Assert.Equal("first report", (await ReadArchiveAsync(diagnostics, first))["debug-disconnect-test.txt"]);
        Assert.Equal(endedAt, (await diagnostics.ListAsync(TestContext.Current.CancellationToken)).Single(session => session.SessionId == first).EndedAt);
    }

    [Fact]
    public async Task BusySessionDoesNotBlockOtherSessionsAndWaitersCanCancel()
    {
        var diagnostics = Create();
        var first = await diagnostics.BeginAsync("first", "", TestContext.Current.CancellationToken);
        var second = await diagnostics.BeginAsync("second", "", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        using (await GetSessionLock(diagnostics, first).LockAsync(TestContext.Current.CancellationToken))
        {
            var download = diagnostics.DownloadAsync(first, cancellation.Token);
            var write = diagnostics.WriteOutputAsync(first, "stdout", "canceled output", cancellation.Token);
            Assert.False(download.IsCompleted);
            Assert.False(write.IsCompleted);
            Assert.Equal(2, (await diagnostics.ListAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)).Count);
            await diagnostics.WriteOutputAsync(second, "stdout", "other session", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        }
        Assert.DoesNotContain("console-stdout.log", (await ReadArchiveAsync(diagnostics, first)).Keys);
        Assert.Equal("other session", (await ReadArchiveAsync(diagnostics, second))["console-stdout.log"]);
    }

    [Fact]
    public async Task ConcurrentSessionsRespectTheSharedStorageBudget()
    {
        var diagnostics = Create(maximumSessionMb: 1, maximumTotalMb: 1);
        var first = await diagnostics.BeginAsync("first", "", TestContext.Current.CancellationToken);
        var second = await diagnostics.BeginAsync("second", "", TestContext.Current.CancellationToken);
        await Task.WhenAll(new[] { first, second }.Select(async id =>
        {
            for (var index = 0; index < 32; index++)
                await diagnostics.WriteOutputAsync(id, "stdout", new string('x', 65536), TestContext.Current.CancellationToken);
        })).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(Directory.EnumerateFiles(Path.Combine(_directory, "evidence"), "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) <= 1024 * 1024);
        Assert.Contains(await diagnostics.ListAsync(TestContext.Current.CancellationToken), session => session.Warnings.Any(warning => warning.Contains("size limit", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RetentionWaitDoesNotHoldTheSessionLockAndCanBeCanceled()
    {
        var diagnostics = Create(maximumSessions: 1);
        var first = await diagnostics.BeginAsync("first", "", TestContext.Current.CancellationToken);
        var field = typeof(SessionDiagnostics).GetField("_retentionLock", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var retentionLock = Assert.IsType<AsyncLock>(field.GetValue(diagnostics));
        using var cancellation = new CancellationTokenSource();
        using (await retentionLock.LockAsync(TestContext.Current.CancellationToken))
        {
            var completion = diagnostics.CompleteAsync(first, cancellation.Token);
            await diagnostics.WriteOutputAsync(first, "stdout", "late output", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.False(completion.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        }
        var second = await diagnostics.BeginAsync("second", "", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await diagnostics.CompleteAsync(second, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private static AsyncLock GetSessionLock(SessionDiagnostics diagnostics, Guid id)
    {
        var field = typeof(SessionDiagnostics).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var sessions = Assert.IsAssignableFrom<IDictionary>(field.GetValue(diagnostics));
        var session = sessions[id];
        Assert.NotNull(session);
        var property = session.GetType().GetProperty("Lock");
        Assert.NotNull(property);
        return Assert.IsType<AsyncLock>(property.GetValue(session));
    }

    private sealed class ChunkedReader(string text) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 47)], cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadArchiveAsync(SessionDiagnostics diagnostics, Guid id)
    {
        var bytes = await diagnostics.DownloadAsync(id, CancellationToken.None);
        Assert.NotNull(bytes);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        var files = new Dictionary<string, string>();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            files[entry.FullName] = await reader.ReadToEndAsync();
        }
        return files;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
