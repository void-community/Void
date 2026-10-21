using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.IntegrationTests.Infrastructure.Harness.Sides;

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
            Console.WriteLine(await CollectLogsAsync(voidLogWindowStartedAt, sides));
            throw new IntegrationTestException($"Test execution failed: {exception.Message}", exception);
        }
        finally
        {
            foreach (var voidProxy in voidProxies)
                voidProxy.ClearLogs();
        }
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
