namespace Void.IntegrationTests.Infrastructure.Harness.Sides;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.IntegrationTests.Infrastructure.IO;
using Void.Proxy;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Links;
using Void.Proxy.Api.Plugins;
using Xunit;

public record VoidProxy(CollectingTextWriter LogWriter, VoidEntryPoint.RunResult RunResult, CancellationTokenSource CancellationTokenSource) : IIntegrationSide
{
    private readonly List<(DateTime From, DateTime Until)> _checkedWindows = [];

    public string LogFileName => "void-proxy.log";
    public IEnumerable<string> Logs => LogWriter.Lines;
    public int Port => RunResult.ListeningPort;

    /// <summary>The plugin service of the proxy, kept from its start, so a proxy that stopped still prints.</summary>
    public required IPluginService Plugins { get; init; }

    /// <summary>The directory the proxy keeps its settings and plugins in.</summary>
    public string WorkingDirectory { get; private init; } = string.Empty;

    /// <summary>The plugin containers the proxy loaded as it started, in the order it loaded them.</summary>
    public IReadOnlyList<string> StartupContainers { get; private init; } = [];

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

        var plugins = result.Services.GetRequiredService<IPluginService>();

        return new VoidProxy(logWriter, result, cancellationTokenSource) { Plugins = plugins, WorkingDirectory = workingDirectory, StartupContainers = [.. plugins.Containers] };
    }

    /// <summary>Runs a command on the proxy's console, and waits until the proxy logged a line that answers it.</summary>
    public async Task<string> RunConsoleCommandAsync(string command, Func<string, bool> isAnswer, CancellationToken cancellationToken = default)
    {
        // The console runs commands in the background, so its answer shows that it ran.
        var answerTask = LogWriter.WaitForLineAsync(isAnswer, cancellationToken);
        var result = await RunResult.Services.GetRequiredService<ICommandService>().ExecuteAsync(RunResult.Services.GetRequiredService<IConsoleService>(), command, cancellationToken);

        if (result is not CommandExecutionResult.Executed)
            throw new IntegrationTestException($"The proxy's console didn't run {command}: {result}");

        return await answerTask;
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
        var unexpectedLogs = SelectEntriesAtOrAbove(LogWriter.GetLinesSince(since), minimumLogLevel);

        if (unexpectedLogs.Length > 0)
            Assert.Fail($"Void emitted {minimumLogLevel} or higher logs:\n{string.Join('\n', unexpectedLogs)}");
    }

    /// <summary>Records that a test checked what the proxy logged while it ran, which its own result then reports.</summary>
    public void MarkChecked(DateTime from, DateTime until)
    {
        lock (_checkedWindows)
            _checkedWindows.Add((from, until));
    }

    /// <summary>Asserts that the proxy logged no warning or worse since a time, other than while a test that checks it ran.</summary>
    public void AssertNoUncheckedWarningOrHigherLogsSince(DateTime since)
    {
        (DateTime From, DateTime Until)[] checkedWindows;

        lock (_checkedWindows)
            checkedWindows = [.. _checkedWindows];

        var unexpectedLogs = SelectEntriesAtOrAbove(LogWriter.GetLinesSince(since, timestamp => Array.Exists(checkedWindows, window => timestamp >= window.From && timestamp <= window.Until)), LogLevel.Warning);

        if (unexpectedLogs.Length > 0)
            Assert.Fail($"Void emitted Warning or higher logs outside of tests:\n{string.Join('\n', unexpectedLogs)}");
    }

    // An entry's exception follows it on lines of no level of their own, which tell why it was logged
    private static string[] SelectEntriesAtOrAbove(IEnumerable<string> lines, LogLevel minimumLogLevel)
    {
        var selected = new List<string>();
        var selecting = false;

        foreach (var line in lines)
        {
            if (GetLogLevel(line) is { } logLevel)
                selecting = logLevel >= minimumLogLevel;

            if (selecting)
                selected.Add(line);
        }

        return [.. selected];
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
        var linkStopping = false;
        var linkStopped = false;

        // The player's link may finish stopping before or after the player is told to have disconnected, and a player that never got a link
        // only disconnects
        await LogWriter.WaitForLineAsync(line =>
        {
            using var _ = stateLock.EnterScope();

            if (line.Contains("Stopping ", StringComparison.Ordinal) && line.Contains($" link {username} <=> ", StringComparison.Ordinal))
                linkStopping = true;
            else if (linkStopping && line.Contains($"Completed invoking {nameof(LinkStoppedEvent)} event", StringComparison.Ordinal))
                linkStopped = true;
            else if (line.Contains($"Player {username} disconnected", StringComparison.Ordinal))
                disconnected = true;

            return disconnected && (!linkStopping || linkStopped);
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

            if (line.Contains($"connected to {serverName}", StringComparison.Ordinal))
                connected = true;

            // The proxy tells the player answered a keep-alive of the server once it loaded into the server, which no plugin a test reloads
            // as the player joins tells. A keep-alive forwarded to the server tells it too once the player joined, as a player configuring
            // forwards them as well.
            bool answerForwarded = connected && line.Contains("Forwarding Keep Alive response ", StringComparison.Ordinal) && line.Contains($" to {serverName}", StringComparison.Ordinal);

            return answerForwarded || line.Contains($"answered a Keep Alive on {serverName}", StringComparison.Ordinal);
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
