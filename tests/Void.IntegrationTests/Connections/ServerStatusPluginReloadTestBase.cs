using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// The server list keeps asking the proxy for the server's status while every container reloads, the protocol of its version included, and
/// each request is answered with the server's status, without keeping a container from unloading. While the protocol of its version is
/// unloaded, the proxy forwards requests to the server as they come, which it warns of, as of any version it doesn't support.
/// </summary>
public abstract class ServerStatusPluginReloadTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(100);

    protected Task RunAsync()
    {
        var startedAt = DateTime.UtcNow;

        return RunReloadingAsync(async () =>
        {
            var failures = new ConcurrentQueue<string>();
            var answers = 0;

            using var reloadedCancellationTokenSource = new CancellationTokenSource();
            var requestsTask = Task.Run(async () =>
            {
                while (!reloadedCancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        var status = await ServerStatus.RequestAsync(Proxy.Port, Fixture.ProtocolVersion, Timeouts.StepTimeoutToken);

                        if (status.Contains("\"version\"", StringComparison.Ordinal))
                            Interlocked.Increment(ref answers);
                        else
                            failures.Enqueue($"Status without a version: {status}");
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                    }

                    await Task.Delay(RequestInterval, CancellationToken.None);
                }
            }, CancellationToken.None);

            try
            {
                foreach (var name in StartupContainers)
                    await ReloadAsync(name, Timeouts.StepTimeoutToken);
            }
            finally
            {
                await reloadedCancellationTokenSource.CancelAsync();
                await requestsTask;
            }

            Assert.Empty(failures);
            Assert.True(answers > 0, "The proxy answered no status request");

            var warnings = Proxy.LogWriter.GetLinesSince(startedAt).Where(line => line.Contains(" WRN] ", StringComparison.Ordinal));
            Assert.All(warnings, warning => Assert.Contains("Channel builder not found", warning, StringComparison.Ordinal));
        }, LogLevel.Error);
    }
}
