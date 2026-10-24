using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Void.IntegrationTests.Infrastructure.Fixtures;
using Void.IntegrationTests.Infrastructure.Harness;
using Void.Minecraft.Commands.Brigadier.Tree;
using Void.Minecraft.Commands.Brigadier.Tree.Nodes;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Services;
using Xunit;

namespace Void.IntegrationTests.Connections;

/// <summary>
/// Unloaded containers leave nothing behind in the proxy, and reloaded ones restore everything they had exactly once: the commands, the
/// listeners, those of a playing player's scope included, and the settings files. A game then plays, and each of its messages reaches its
/// server once, as it would not with a packet registered twice.
/// </summary>
public abstract class PluginLeftoversTestBase(ConnectionsFixture fixture) : PluginReloadTestBase(fixture)
{
    // The proxy's own command, which plugins don't register.
    private const string ProxyCommand = "plugins";

    protected Task RunAsync()
    {
        return RunReloadingAsync(async () =>
        {
            var commands = await ReadCommandsAsync();
            var listeners = ReadListeners();
            var settings = await ReadSettingsAsync();

            foreach (var name in StartupContainers.Reverse())
                await UnloadAsync(name, Timeouts.StepTimeoutToken);

            Assert.All(await ReadCommandsAsync(), command => Assert.StartsWith(ProxyCommand, command, StringComparison.Ordinal));
            Assert.Empty(ReadPluginListeners());

            foreach (var name in StartupContainers)
                await LoadAsync(name, Timeouts.StepTimeoutToken);

            Assert.Equal(commands, await ReadCommandsAsync());
            Assert.Equal(listeners, ReadListeners());
            Assert.Equal(settings, await ReadSettingsAsync());

            await ReloadWhilePlayingAsync();
            Assert.Equal(listeners, ReadListeners());

            await PlayAsync(nameof(PluginLeftoversTestBase), redirect: true);
        });
    }

    // The listeners of a playing player's scope are replaced by those of the reloaded containers, as many as before.
    private async Task ReloadWhilePlayingAsync()
    {
        var containers = FindContainersBesidesProtocol();
        Task playerDisconnectionTask;

        await using (var game = await Fixture.Game1.BeginSessionAsync(nameof(PluginLeftoversTestBase), [Proxy, Fixture.Server1, Fixture.Server2], Timeouts.SetupTimeoutToken))
        {
            playerDisconnectionTask = Proxy.WaitForPlayerDisconnectionAsync(game.Username, Timeouts.SetupTimeoutToken);

            await game.JoinServerAsync(ProxyEndPoint, Timeouts.SetupTimeoutToken);
            await Fixture.Server1.ExpectLoginAsync(game.Username, game.StartedAt, Timeouts.SetupTimeoutToken);
            await game.EnsureStableAsync(Timeouts.SetupTimeoutToken);

            var playingListeners = ReadListeners();

            foreach (var name in containers)
                await ReloadAsync(name, Timeouts.StepTimeoutToken);

            Assert.Equal(playingListeners, ReadListeners());

            await game.EndAsync(Fixture.Server1, Timeouts.StepTimeoutToken);
        }

        await playerDisconnectionTask;
    }

    /// <summary>Every command the console can run, as the words that run it, with <c>*</c> after those that run something.</summary>
    private async Task<string[]> ReadCommandsAsync()
    {
        var services = Proxy.RunResult.Services;
        var root = new RootCommandNode();

        await services.GetRequiredService<ICommandService>().CopyToAsync(root, services.GetRequiredService<IConsoleService>(), Timeouts.StepTimeoutToken);

        return [.. root.Children.SelectMany(node => DescribeCommands(node, prefix: null)).Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> DescribeCommands(CommandNode node, string? prefix)
    {
        var usage = prefix is null ? node.UsageText : $"{prefix} {node.UsageText}";
        IEnumerable<string> self = [node.Executor is null ? usage : $"{usage} *"];

        return self.Concat(node.Children.SelectMany(child => DescribeCommands(child, usage)));
    }

    /// <summary>The types of the registered listeners, each with how many of it are registered.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private string[] ReadListeners()
    {
        return [.. Proxy.RunResult.Services.GetRequiredService<IEventService>().Listeners
            .GroupBy(listener => listener.GetType().FullName)
            .Select(group => $"{group.Key} x{group.Count()}")
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The types of the registered listeners that plugins brought.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private string[] ReadPluginListeners()
    {
        return [.. Proxy.RunResult.Services.GetRequiredService<IEventService>().Listeners
            .Where(listener => AssemblyLoadContext.GetLoadContext(listener.GetType().Assembly)?.IsCollectible is true)
            .Select(listener => $"{listener.GetType().FullName}")];
    }

    /// <summary>Each settings file of the proxy and its plugins, with its contents.</summary>
    private async Task<string[]> ReadSettingsAsync()
    {
        var directory = new DirectoryInfo(Path.Combine(Proxy.WorkingDirectory, "configs"));

        if (!directory.Exists)
            return [];

        var settings = new List<string>();

        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(file => file.FullName, StringComparer.Ordinal))
            settings.Add($"{Path.GetRelativePath(directory.FullName, file.FullName)}:\n{await File.ReadAllTextAsync(file.FullName, Timeouts.StepTimeoutToken)}");

        return [.. settings];
    }
}
