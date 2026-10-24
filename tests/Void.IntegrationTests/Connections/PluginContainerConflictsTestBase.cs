using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Containers refuse what can't be done with them, and stay usable after: unloading or loading one that doesn't exist, loading one that's
/// loaded, unloading one that's unloaded, and unloading or loading the same one twice at once, which does it once. The <c>/plugins</c> command
/// of the console answers each with why, as a warning, and nothing else warns.
/// </summary>
public abstract class PluginContainerConflictsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
        var startedAt = DateTime.UtcNow;
        var unknown = $"missing-{Guid.NewGuid():N}.dll";

        return RunReloadingAsync(async () =>
        {
            var essentials = Assert.Single(StartupContainers, name => name.Contains("Essentials", StringComparison.Ordinal));

            await Assert.ThrowsAnyAsync<Exception>(async () => await Plugins.UnloadContainerAsync(unknown, Timeouts.StepTimeoutToken));
            await Assert.ThrowsAnyAsync<Exception>(async () => await Plugins.LoadContainerAsync(unknown, Timeouts.StepTimeoutToken));

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Plugins.LoadContainerAsync(essentials, Timeouts.StepTimeoutToken));
            Assert.Single(Plugins.Containers, name => name == essentials);

            await UnloadAsync(essentials, Timeouts.StepTimeoutToken);
            await Assert.ThrowsAnyAsync<Exception>(async () => await Plugins.UnloadContainerAsync(essentials, Timeouts.StepTimeoutToken));
            await LoadAsync(essentials, Timeouts.StepTimeoutToken);

            var context = TrackContext(essentials);
            bool[] unloads = await Task.WhenAll(TryAsync(Plugins.UnloadContainerAsync(essentials, Timeouts.StepTimeoutToken)), TryAsync(Plugins.UnloadContainerAsync(essentials, Timeouts.StepTimeoutToken)));
            Assert.Single(unloads, unloaded => unloaded);
            Assert.DoesNotContain(essentials, Plugins.Containers);
            await AssertCollectedAsync(essentials, context, Timeouts.StepTimeoutToken);

            bool[] loads = await Task.WhenAll(TryAsync(Plugins.LoadContainerAsync(essentials, Timeouts.StepTimeoutToken)), TryAsync(Plugins.LoadContainerAsync(essentials, Timeouts.StepTimeoutToken)));
            Assert.Single(loads, loaded => loaded);
            Assert.Single(Plugins.Containers, name => name == essentials);

            await Proxy.RunConsoleCommandAsync($"plugins unload {unknown}", line => line.Contains($"No plugin container matches {unknown}", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await Proxy.RunConsoleCommandAsync("plugins load essentials", line => line.Contains($"Plugin container {essentials} is already loaded", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await Proxy.RunConsoleCommandAsync("plugins unload essentials", line => line.Contains($"Unloaded {essentials}", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await Proxy.RunConsoleCommandAsync("plugins unload essentials", line => line.Contains("No plugin container matches essentials", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await Proxy.RunConsoleCommandAsync("plugins load essentials", line => line.Contains($"Loaded {essentials}", StringComparison.Ordinal), Timeouts.StepTimeoutToken);

            // The command runs one operation at a time, so both reloads at once reload the container.
            var firstReloadTask = Proxy.RunConsoleCommandAsync("plugins reload essentials", line => line.Contains($"Reloaded {essentials}", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            var secondReloadTask = Proxy.RunConsoleCommandAsync("plugins reload essentials", line => line.Contains($"Reloaded {essentials}", StringComparison.Ordinal), Timeouts.StepTimeoutToken);
            await Task.WhenAll(firstReloadTask, secondReloadTask);
            Assert.Single(Plugins.Containers, name => name == essentials);

            await PlayAsync(nameof(PluginContainerConflictsTestBase));

            string[] expectedWarnings = [$"No plugin container matches {unknown}", $"Plugin container {essentials} is already loaded", "No plugin container matches essentials"];
            var warnings = Proxy.LogWriter.GetLinesSince(startedAt).Where(line => line.Contains(" WRN] ", StringComparison.Ordinal)).ToArray();

            Assert.All(warnings, warning => Assert.Contains(expectedWarnings, expectedWarning => warning.Contains(expectedWarning, StringComparison.Ordinal)));
        }, LogLevel.Error);
    }

    private static async Task<bool> TryAsync(ValueTask operation)
    {
        try
        {
            await operation;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.WriteLine($"[Void.IntegrationTests] Plugin container operation failed: {exception.Message}");
            return false;
        }
    }
}
