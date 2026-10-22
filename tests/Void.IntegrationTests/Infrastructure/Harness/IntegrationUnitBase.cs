using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness.Sides;
using Xunit;

namespace Void.IntegrationTests.Infrastructure.Harness;

public class IntegrationUnitBase
{
    // Forge servers break down by races of their own, which no proxy prevents: Forge before Minecraft 1.13 sends packets it hasn't registered
    // yet, and later Forge changes the list of its login messages while it sends them.
    private static readonly string[] ServerRaces = ["Can't serialize unregistered packet", "lost connection: Internal Exception: java.util.ConcurrentModificationException"];

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
            Console.WriteLine(await CollectLogsAsync(voidLogWindowStartedAt, sides));
            await SaveServerDebugLogsAsync(sides);
            throw new IntegrationTestException($"Test execution failed: {exception.Message}", exception);
        }
        finally
        {
            // The test reports what the proxy logged while it ran, so its collection checks only what the proxy logged around tests.
            foreach (var voidProxy in voidProxies)
                voidProxy.MarkChecked(voidLogWindowStartedAt, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Runs a test, and once more when one of its servers broke down by a race of its own meanwhile, which the test then shows as a warning.
    /// A join through the proxy that such a server drops goes on to another server, so the whole test runs again rather than the join.
    /// </summary>
    public static async Task RetryAfterServerRaceAsync(Func<Task> test, IReadOnlyList<MinecraftServer> servers)
    {
        var startedAt = DateTime.UtcNow;

        try
        {
            await test();
        }
        catch (Exception exception) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            if (await FindServerRaceAsync(servers, startedAt) is not { } race)
                throw;

            var firstFailure = exception.Message.Split('\n', 2)[0];
            var warning = $"A server broke down by its own race, so the test ran again: {race}\nThe first run failed: {firstFailure}";
            TestContext.Current.AddWarning(warning);
            Console.WriteLine($"[Void.IntegrationTests] {warning}");

            try
            {
                await test();
            }
            catch (Exception retryException)
            {
                throw new IntegrationTestException($"Test failed again after a server's own race: {retryException.Message}\nIts first run failed: {firstFailure}", retryException);
            }
        }
    }

    // Modded servers log everything at debug level, every packet included, into a file, which a failed test keeps in its steps.
    private static async Task SaveServerDebugLogsAsync(IIntegrationSide[] sides)
    {
        var testName = string.Join('_', (TestContext.Current.Test?.TestDisplayName ?? "Unknown").Split(Path.GetInvalidFileNameChars()));
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "steps", "ServerDebugLogs", testName, DateTime.UtcNow.ToString("HHmmss"));

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

    private static async Task<string?> FindServerRaceAsync(IReadOnlyList<MinecraftServer> servers, DateTime since)
    {
        foreach (var server in servers)
        {
            try
            {
                var logs = await server.ReadLogsAsync(since, Timeouts.StepTimeoutToken);

                if (logs.FirstOrDefault(line => Array.Exists(ServerRaces, race => line.Contains(race, StringComparison.Ordinal))) is { } raceLine)
                    return raceLine.Trim();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[Void.IntegrationTests] {server.LogFileName} couldn't be read for races: {exception.Message}");
            }
        }

        return null;
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
