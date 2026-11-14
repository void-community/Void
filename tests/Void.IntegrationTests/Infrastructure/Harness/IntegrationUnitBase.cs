using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Harness;

public class IntegrationUnitBase
{
    public static Task LoggedExecutorAsync(Func<Task> function, params IIntegrationSide[] sides)
    {
        return LoggedExecutorAsync(function, LogLevel.Warning, sides);
    }

    public static async Task LoggedExecutorAsync(Func<Task> function, LogLevel minimumFailureLogLevel, params IIntegrationSide[] sides)
    {
        var voidLogWindowStartedAt = DateTime.UtcNow;
        var voidProxies = sides.OfType<VoidProxy>().Distinct().ToArray();

        try
        {
            await function();

            foreach (var voidProxy in voidProxies)
                voidProxy.AssertNoLogsAtOrAboveSince(voidLogWindowStartedAt, minimumFailureLogLevel);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Primary integration failure:\n{exception}");
            Console.WriteLine(await DescribeMemoryAsync());
            Console.WriteLine(await CollectLogsAsync(voidLogWindowStartedAt, sides));
            await SaveServerDebugLogsAsync(sides);
            await SaveProxyLogsAsync(voidProxies);
            throw new IntegrationTestException($"Test execution failed: {exception.Message}", exception);
        }
        finally
        {
            // The test reports what the proxy logged while it ran, so its collection checks only what the proxy logged around tests.
            foreach (var voidProxy in voidProxies)
                voidProxy.MarkChecked(voidLogWindowStartedAt, DateTime.UtcNow);
        }
    }

    // A runner that runs short of memory slows every game, server and process on it, which fails a test in whatever it waited on, so a failed
    // test tells how much memory the runner had left and how much each container used
    private static async Task<string> DescribeMemoryAsync()
    {
        if (!OperatingSystem.IsLinux())
            return string.Empty;

        var description = new StringBuilder("Memory:\n");

        try
        {
            var memory = await File.ReadAllLinesAsync("/proc/meminfo");
            description.AppendJoin('\n', memory.Where(line => line.StartsWith("MemTotal:") || line.StartsWith("MemAvailable:") || line.StartsWith("SwapTotal:") || line.StartsWith("SwapFree:"))).Append('\n');

            if (File.Exists("/proc/pressure/memory"))
                description.Append("Pressure: ").AppendJoin("; ", await File.ReadAllLinesAsync("/proc/pressure/memory")).Append('\n');

            using var docker = Process.Start(new ProcessStartInfo("docker", ["stats", "--no-stream", "--format", "{{.Name}} {{.MemUsage}} {{.CPUPerc}}"]) { RedirectStandardOutput = true });

            if (docker is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                description.Append(await docker.StandardOutput.ReadToEndAsync(timeout.Token));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException)
        {
            description.Append($"Memory couldn't be told: {exception.Message}");
        }

        return description.ToString();
    }

    // Modded servers log everything at debug level, every packet included, into a file, which a failed test keeps in its steps.
    private static async Task SaveServerDebugLogsAsync(IIntegrationSide[] sides)
    {
        var directory = GetFailureLogsDirectory("ServerDebugLogs");

        foreach (var server in sides.OfType<ModdedServer>().Distinct())
        {
            try
            {
                await server.SaveDebugLogAsync(directory, Timeouts.StepTimeoutToken);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Void.IntegrationTests] The debug log of {server.LogFileName} couldn't be saved: {exception.Message}");
            }
        }
    }

    // A proxy serves every test of its collection, and what an earlier test left behind, such as a connection that never ended, can fail a
    // later one, so a failed test keeps all the proxy logged since it started.
    private static async Task SaveProxyLogsAsync(VoidProxy[] voidProxies)
    {
        var directory = GetFailureLogsDirectory("ProxyLogs");

        foreach (var voidProxy in voidProxies)
        {
            try
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllLinesAsync(Path.Combine(directory, voidProxy.LogFileName), await voidProxy.ReadLogsAsync(DateTime.MinValue, Timeouts.StepTimeoutToken), Timeouts.StepTimeoutToken);
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Void.IntegrationTests] The log of {voidProxy} couldn't be saved: {exception.Message}");
            }
        }
    }

    private static string GetFailureLogsDirectory(string kind)
    {
        var testName = string.Join('_', (TestContext.Current.Test?.TestDisplayName ?? "Unknown").Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Directory.GetCurrentDirectory(), "steps", kind, testName, DateTime.UtcNow.ToString("HHmmss"));
    }

    // Sides outlive the test, so only what they logged while it ran belongs to its failure.
    private static async Task<string> CollectLogsAsync(DateTime since, IIntegrationSide[] sides)
    {
        var logs = new List<string>(sides.Length);

        foreach (var side in sides)
        {
            try
            {
                logs.Add($"{side} logs:\n{string.Join("\n", await side.ReadLogsAsync(since, Timeouts.StepTimeoutToken))}");
            }
            catch (Exception exception)
            {
                logs.Add($"{side} logs couldn't be read: {exception.Message}");
            }
        }

        return $"Logs:\n\n\n{string.Join("\n\n\n", logs)}";
    }
}
