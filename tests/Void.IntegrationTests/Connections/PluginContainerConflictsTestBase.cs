using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Containers refuse what can't be done with them, and stay usable after: unloading or loading one that doesn't exist, loading one that's
/// loaded, unloading one that's unloaded, and unloading or loading the same one twice at once, which does it once. The <c>/plugins</c> command
/// of the console answers each with why, and suggests the containers each of its subcommands takes.
/// </summary>
public abstract class PluginContainerConflictsTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    protected Task RunAsync()
    {
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

            Assert.Contains(essentials, await SuggestAsync("plugins unload ess"));
            Assert.Contains(essentials, await SuggestAsync("plugins reload ESS"));
            Assert.DoesNotContain(essentials, await SuggestAsync("plugins load "));

            await UnloadAsync(essentials, Timeouts.StepTimeoutToken);
            Assert.Contains(essentials, await SuggestAsync("plugins load essentials"));
            Assert.DoesNotContain(essentials, await SuggestAsync("plugins unload "));
            await LoadAsync(essentials, Timeouts.StepTimeoutToken);

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
        });
    }

    private async Task<string[]> SuggestAsync(string input)
    {
        var services = Proxy.RunResult.Services;
        var suggestions = await services.GetRequiredService<ICommandService>().SuggestAsync(input, services.GetRequiredService<IConsoleService>(), Timeouts.StepTimeoutToken);

        return [.. suggestions.Select(suggestion => suggestion.Text)];
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
