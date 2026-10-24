using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using Nito.AsyncEx;
using Void.Proxy.Api;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Proxy;
using Void.Proxy.Api.Plugins;
using Void.Terminal;
using SystemConsole = System.Console;

namespace Void.Proxy.Console;

public class ConsoleService(ILogger<ConsoleService> logger, ConsoleConfiguration consoleConfiguration, IRunOptions runOptions, ICommandService commands) : IConsoleService, IEventListener
{
    private readonly AsyncLock _optionDiscoveryLock = new();

    // Options a plugin discovers while it loads belong to it and leave with it, so the plugin can discover them again when it loads again.
    private readonly Dictionary<IPlugin, List<Option>> _pluginOptions = [];
    private IPlugin? _loadingPlugin;

    private readonly PromptReader _reader = new();
    public static bool IsEnabled => !SystemConsole.IsInputRedirected && !SystemConsole.IsOutputRedirected;
    public IEnumerable<Option> DiscoveredOptions => consoleConfiguration.RootCommand.Options;

    public bool TryGetOptionValue<TValue>(Option<TValue> option, [MaybeNullWhen(false)] out TValue value)
    {
        EnsureOptionDiscovered(option);

        var parseResult = consoleConfiguration.RootCommand.Parse(runOptions.Arguments);
        var optionResult = parseResult.GetResult(option);

        value = parseResult.GetValue(option);
        return value is not null && optionResult is not null && !optionResult.Implicit;
    }

    public TValue? GetOptionValue<TValue>(Option<TValue> option)
    {
        EnsureOptionDiscovered(option);
        return consoleConfiguration.RootCommand.Parse(runOptions.Arguments).GetValue(option);
    }

    public TValue GetRequiredOptionValue<TValue>(Option<TValue> option)
    {
        EnsureOptionDiscovered(option);
        return consoleConfiguration.RootCommand.Parse(runOptions.Arguments).GetRequiredValue(option);
    }

    public void EnsureOptionDiscovered(Option option)
    {
        using var lockDisposable = _optionDiscoveryLock.Lock();

        if (consoleConfiguration.RootCommand.Options.Contains(option))
            return;

        foreach (var existingOption in consoleConfiguration.RootCommand.Options)
        {
            var existingAliases = existingOption.Aliases ?? [];
            var optionAliases = option.Aliases ?? [];

            if (!existingOption.Name.Equals(option.Name, StringComparison.OrdinalIgnoreCase) && !existingAliases.Any(alias => optionAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)))
                continue;

            throw new InvalidOperationException($"Option with name or alias '{option.Name}' already exists. " +
                                                $"Discovered: {option.Name} ({string.Join(", ", optionAliases)}), " +
                                                $"Existing: {existingOption.Name} ({string.Join(", ", existingAliases)})");
        }

        var options = consoleConfiguration.RootCommand.Options;

        // Find the position where the new option should be inserted so that the list
        // remains ordered by two keys:
        //   1. Alias count, in descending order (options with more aliases come first)
        //   2. Name, in ascending order, case-insensitive
        //
        // The trick: build a tuple (-AliasCount, NameLowercase). Negating the alias count
        // flips the order so higher counts sort first. We then CompareTo the new option’s
        // tuple and TakeWhile all existing entries that are "less" than it, leaving us
        // with the correct insert index.

        var insertIndex = options.TakeWhile(existing => (-(existing.Aliases?.Count ?? 0), existing.Name?.ToLowerInvariant() ?? string.Empty).CompareTo((-(option.Aliases?.Count ?? 0), option.Name?.ToLowerInvariant() ?? string.Empty)) < 0).Count();
        options.Insert(insertIndex, option);

        if (_loadingPlugin is not { } plugin)
            return;

        if (!_pluginOptions.TryGetValue(plugin, out var pluginOptions))
            _pluginOptions[plugin] = pluginOptions = [];

        pluginOptions.Add(option);
    }

    // Plugins load one at a time, so the options discovered until a plugin has loaded are its own.
    [Subscribe(PostOrder.First)]
    public void OnPluginLoading(PluginLoadingEvent @event)
    {
        using var lockDisposable = _optionDiscoveryLock.Lock();
        _loadingPlugin = @event.Plugin;
    }

    [Subscribe]
    public void OnPluginLoaded(PluginLoadedEvent @event)
    {
        using var lockDisposable = _optionDiscoveryLock.Lock();

        if (_loadingPlugin == @event.Plugin)
            _loadingPlugin = null;
    }

    [Subscribe]
    public void OnPluginUnloading(PluginUnloadingEvent @event)
    {
        using var lockDisposable = _optionDiscoveryLock.Lock();

        if (_loadingPlugin == @event.Plugin)
            _loadingPlugin = null;

        if (!_pluginOptions.Remove(@event.Plugin, out var pluginOptions))
            return;

        foreach (var option in pluginOptions)
            consoleConfiguration.RootCommand.Options.Remove(option);
    }

    public async ValueTask HandleCommandsAsync(CancellationToken cancellationToken = default)
    {
        if (!consoleConfiguration.HasTerminal || !IsEnabled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        var command = await _reader.ReadLineAsync(async (input, readLineCancellationToken) =>
        {
            var suggestions = await SuggestAsync(input, readLineCancellationToken);
            return suggestions.Select(suggestion => suggestion.Text).ToArray();
        }, cancellationToken);

        logger.LogInformation("Proxy issued command: {Command}", command);

        if (string.IsNullOrWhiteSpace(command))
            return;

        try
        {
            await commands.ExecuteAsync(this, command, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError("{Exception}", exception);
        }
    }

    [Subscribe(PostOrder.Last)]
    public void OnProxyStarting(ProxyStartingEvent @event)
    {
        if (!consoleConfiguration.HasTerminal)
            return;

        SystemConsole.SetOut(_reader.TextWriter);

        if (!IsEnabled)
            return;

        _reader.ResetStyle();
        _reader.HideCursor();
    }

    [Subscribe]
    public void OnProxyStopping(ProxyStoppingEvent @event)
    {
        if (!consoleConfiguration.HasTerminal)
            return;

        if (!IsEnabled)
            return;

        _reader.ResetStyle();
        _reader.ShowCursor();
    }

    public ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("{Message}", message);
        return ValueTask.CompletedTask;
    }

    public override string ToString()
    {
        return nameof(Console);
    }

    private async ValueTask<IEnumerable<ICommandSuggestion>> SuggestAsync(string input, CancellationToken cancellationToken = default)
    {
        return await commands.SuggestAsync(input, this, cancellationToken);
    }
}
