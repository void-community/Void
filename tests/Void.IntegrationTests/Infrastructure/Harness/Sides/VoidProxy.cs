namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.IO;
using Void.Proxy;
using Void.Proxy.Api.Events.Links;
using Xunit;

public record VoidProxy(CollectingTextWriter LogWriter, VoidEntryPoint.RunResult RunResult, CancellationTokenSource CancellationTokenSource) : IIntegrationSide
{
    public string LogFileName => "void-proxy.log";
    public IEnumerable<string> Logs => LogWriter.Lines;
    public int Port => RunResult.ListeningPort;

    public static Task<VoidProxy> CreateAsync(string workingDirectory, string targetServer, int proxyPort = 0, bool ignoreFileServers = true, bool offlineMode = true, CancellationToken cancellationToken = default)
    {
        return CreateAsync(workingDirectory, [targetServer], proxyPort, ignoreFileServers, offlineMode, cancellationToken);
    }

    public static async Task<VoidProxy> CreateAsync(string workingDirectory, IEnumerable<string> targetServers, int proxyPort = 0, bool ignoreFileServers = true, bool offlineMode = true, CancellationToken cancellationToken = default)
    {
        var logWriter = new CollectingTextWriter();
        var cancellationTokenSource = new CancellationTokenSource();

        workingDirectory = Path.Combine(workingDirectory, nameof(VoidProxy));

        if (!Directory.Exists(workingDirectory))
            Directory.CreateDirectory(workingDirectory);

        var args = new List<string>
        {
            "--port", proxyPort.ToString(),
            "--logging", "Trace"
        };

        foreach (var targetServer in targetServers)
        {
            args.Add("--server");
            args.Add(targetServer);
        }

        if (ignoreFileServers)
            args.Add("--ignore-file-servers");

        if (offlineMode)
            args.Add("--offline");

        var result = await VoidEntryPoint.RunAsync(new VoidEntryPoint.RunOptions { WorkingDirectory = workingDirectory, Arguments = [.. args], LogWriter = logWriter }, cancellationTokenSource.Token);

        // Wait for the proxy to start, because it takes some time to listen on the port

        try
        {
            while (logWriter.Lines.All(line => !line.Contains("Proxy started")))
                await Task.Delay(1_000, cancellationToken);
        }
        catch (Exception exception)
        {
            Assert.Fail($"{nameof(VoidProxy)} failed to start. Logs:\n{logWriter.Text}\n{exception}");
        }

        return new VoidProxy(logWriter, result, cancellationTokenSource);
    }

    public void ClearLogs()
    {
        LogWriter.Clear();
    }

    public Task<IEnumerable<string>> ReadLogsAsync(DateTime since, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IEnumerable<string>>(LogWriter.GetLinesSince(since));
    }

    public void AssertNoWarningOrHigherLogsSince(DateTime since)
    {
        AssertNoLogsAtOrAboveSince(since, LogLevel.Warning);
    }

    public void AssertNoLogsAtOrAboveSince(DateTime since, LogLevel minimumLogLevel)
    {
        var unexpectedLogs = GetLogsAtOrAboveSince(since, minimumLogLevel);

        if (unexpectedLogs.Length > 0)
            Assert.Fail($"Void emitted {minimumLogLevel} or higher logs:\n{string.Join('\n', unexpectedLogs)}");
    }

    public string[] GetLogsAtOrAboveSince(DateTime since, LogLevel minimumLogLevel)
    {
        return [.. LogWriter.GetLinesSince(since).Where(line => GetLogLevel(line) is { } logLevel && logLevel >= minimumLogLevel)];
    }

    private static LogLevel? GetLogLevel(string line)
    {
        if (line.Contains(" VRB] ", StringComparison.Ordinal))
            return LogLevel.Trace;

        if (line.Contains(" DBG] ", StringComparison.Ordinal))
            return LogLevel.Debug;

        if (line.Contains(" INF] ", StringComparison.Ordinal))
            return LogLevel.Information;

        if (line.Contains(" WRN] ", StringComparison.Ordinal))
            return LogLevel.Warning;

        if (line.Contains(" ERR] ", StringComparison.Ordinal))
            return LogLevel.Error;

        if (line.Contains(" FTL] ", StringComparison.Ordinal))
            return LogLevel.Critical;

        return null;
    }

    public async Task WaitForPlayerDisconnectionAsync(string username, CancellationToken cancellationToken = default)
    {
        var stateLock = new Lock();
        var disconnected = false;

        await LogWriter.WaitForLineAsync(line =>
        {
            using var _ = stateLock.EnterScope();

            if (!disconnected)
            {
                disconnected = line.Contains($"Player {username} disconnected", StringComparison.Ordinal);
                return false;
            }

            return line.Contains($"Completed invoking {nameof(LinkStoppedEvent)} event", StringComparison.Ordinal);
        }, cancellationToken);
    }

    public async Task WaitForServerConnectionAndKeepAliveAsync(string username, string serverName, CancellationToken cancellationToken = default)
    {
        var stateLock = new Lock();
        var connected = false;

        using var waiterCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readyTask = LogWriter.WaitForLineAsync(line =>
        {
            using var _ = stateLock.EnterScope();

            if (!line.Contains(username, StringComparison.Ordinal))
                return false;

            if (!connected)
            {
                connected = line.Contains($"connected to {serverName}", StringComparison.Ordinal);
                return false;
            }

            // The player answered a keep-alive of the server: forwarded to it, or the first answer SlowJoin waits for after the join.
            bool answerForwarded = line.Contains("Forwarding Keep Alive response ", StringComparison.Ordinal) && line.Contains($" to {serverName}", StringComparison.Ordinal);

            return answerForwarded || line.Contains($"answered its first Keep Alive on {serverName}", StringComparison.Ordinal);
        }, waiterCancellationTokenSource.Token);
        var disconnectedTask = LogWriter.WaitForLineAsync(
            line => line.Contains($"Player {username} disconnected", StringComparison.Ordinal),
            waiterCancellationTokenSource.Token);

        try
        {
            var completedTask = await Task.WhenAny(readyTask, disconnectedTask);

            if (completedTask == disconnectedTask)
            {
                var disconnectedLine = await disconnectedTask;
                throw new IntegrationTestException($"Player {username} disconnected before becoming ready on {serverName}. {GetState()} Log: {disconnectedLine}");
            }

            await readyTask;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new IntegrationTestException($"Timed out waiting for player {username} to become ready on {serverName}. {GetState()}", exception);
        }
        finally
        {
            await waiterCancellationTokenSource.CancelAsync();
            await Task.WhenAll((Task)readyTask, disconnectedTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        string GetState()
        {
            using var _ = stateLock.EnterScope();
            return $"Connected: {connected}.";
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        await CancellationTokenSource.CancelAsync();
        await RunResult.CompletionTask;
        await RunResult.DisposeAsync();
    }
}
