using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nito.AsyncEx;
using Nito.Disposables.Internals;
using Void.Minecraft.Commands.Brigadier;
using Void.Minecraft.Commands.Brigadier.Context;
using Void.Minecraft.Commands.Brigadier.Extensions;
using Void.Minecraft.Commands.Brigadier.Suggestion;
using Void.Proxy.Api;
using Void.Proxy.Api.Commands;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Proxy;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Extensions;
using Void.Proxy.Plugins.Containers;
using Void.Proxy.Plugins.Context;
using Void.Proxy.Plugins.Retention;

namespace Void.Proxy.Plugins;

public class PluginService(ILogger<PluginService> logger, IRunOptions runOptions, IEventService events, IDependencyService dependencies, IConsoleService consoleService, ICommandService commands, HttpClient httpClient) : IPluginService, IEventListener
{
    public const string DefaultPluginsPath = "plugins";

    private const string ContainerArgument = "name";

    private static readonly Option<string[]> PluginsOption = new("--plugin", "-p") { Description = "Provides a path to the file, directory or URL to load plugin." };

    // Plugins load concurrently while their dependencies resolve from other containers, so readers work on snapshots instead of locks.
    private readonly CopyOnWriteCollection<WeakPluginContainer> _containers = new();
    private readonly TimeSpan _gcRate = TimeSpan.FromMilliseconds(500);

    private readonly AsyncLock _lock = new();
    private readonly TimeSpan _unloadTimeout = TimeSpan.FromSeconds(10);

    public IEnumerable<IPlugin> All => _containers.Snapshot.SelectMany(container => container.Plugins);

    public IEnumerable<string> Containers => _containers.Snapshot
        .Select(container => container.Context.Name)
        .WhereNotNull();

    public IEnumerable<string> LoadableContainers => GetDirectoryPluginNames().Concat(GetEmbeddedPluginNames()).Distinct();

    public async ValueTask LoadPluginsAsync(string path = DefaultPluginsPath, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Loading environment plugins");
        await LoadEnvironmentPluginsAsync(cancellationToken);

        logger.LogDebug("Loading embedded plugins");
        await LoadEmbeddedPluginsAsync(cancellationToken);

        logger.LogDebug("Loading directory plugins");
        await LoadDirectoryPluginsAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask LoadEnvironmentPluginsAsync(CancellationToken cancellationToken = default)
    {
        var plugins = await GetArgumentsPlugins()
            .Concat(GetVariablesPlugins())
            .Select(async variable =>
            {
                if (Uri.TryCreate(variable, UriKind.Absolute, out var url))
                {
                    if (url.IsFile)
                    {
                        var name = Path.GetFileName(url.LocalPath);
                        logger.LogTrace("Found {Name} local plugin", name);

                        try
                        {
                            await using var stream = File.OpenRead(url.LocalPath);
                            return LoadContainer(name, stream);
                        }
                        catch (Exception exception)
                        {
                            logger.LogWarning("Local plugin {Name} couldn't be loaded: {Message}", name, exception.Message);
                            return null;
                        }
                    }

                    var remoteName = url.LocalPath;
                    logger.LogTrace("Found {Name} remote plugin", remoteName);

                    try
                    {
                        using var response = await httpClient.GetAsync(url, cancellationToken);

                        if (!response.IsSuccessStatusCode)
                        {
                            logger.LogWarning("Remote plugin {Uri} couldn't be loaded: {StatusCode}", url, response.StatusCode);
                            return null;
                        }

                        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        return LoadContainer(remoteName, stream);
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning("Remote plugin {Uri} couldn't be loaded: {Message}", url, exception.Message);
                        return null;
                    }
                }

                if (!Path.IsPathRooted(variable))
                    variable = Path.Combine(runOptions.WorkingDirectory, variable);

                if (File.Exists(variable))
                {
                    var name = Path.GetFileName(variable);
                    logger.LogTrace("Found {Name} local plugin", name);

                    await using var stream = File.OpenRead(variable);
                    return LoadContainer(name, stream);
                }
                else if (Directory.Exists(variable))
                {
                    return await LoadDirectoryPluginTypesAsync(new DirectoryInfo(variable), cancellationToken);
                }
                else
                {
                    return null;
                }
            })
            .WhenAll();

        await LoadPluginsAsync(plugins.WhereNotNull().SelectMany(x => x), cancellationToken);

        string[] GetArgumentsPlugins() => consoleService.GetOptionValue(PluginsOption) ?? [];

        static IEnumerable<string> GetVariablesPlugins()
        {
            var args = Environment.GetEnvironmentVariable("VOID_PLUGINS");

            if (string.IsNullOrWhiteSpace(args))
                return [];

            return args.SplitInput();
        }
    }

    public async ValueTask LoadEmbeddedPluginsAsync(CancellationToken cancellationToken = default)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var plugins = GetEmbeddedPluginNames().SelectMany(name =>
        {
            logger.LogTrace("Found {Name} embedded plugin", name);

            if (assembly.GetManifestResourceStream(name) is not { } stream)
            {
                logger.LogWarning("Embedded plugin {Name} couldn't be loaded", name);
                return [];
            }

            try
            {
                return LoadContainer(name, stream, ignoreEmpty: true);
            }
            finally
            {
                stream.Close();
            }
        });

        await LoadPluginsAsync(plugins, cancellationToken);
    }

    public async ValueTask LoadDirectoryPluginsAsync(string path = DefaultPluginsPath, CancellationToken cancellationToken = default)
    {
        path = GetDirectoryPath(path);

        var pluginsDirectoryInfo = new DirectoryInfo(path);

        if (!pluginsDirectoryInfo.Exists)
        {
            try
            {
                pluginsDirectoryInfo.Create();
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogWarning("Cannot create {Path} plugins path: {Message}", path, exception.Message);
            }

            return;
        }

        var plugins = await LoadDirectoryPluginTypesAsync(pluginsDirectoryInfo, cancellationToken);
        await LoadPluginsAsync(plugins, cancellationToken);
    }

    public async ValueTask LoadContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        // Saves reading the assembly of a loaded container, which TryLoadContainer would refuse anyway.
        if (Containers.Contains(name))
            throw new InvalidOperationException($"Plugin container {name} is already loaded");

        bool loaded;
        IReadOnlyList<Type> plugins;
        var path = Path.Combine(GetDirectoryPath(DefaultPluginsPath), name);

        // A plugin file takes the place of the embedded plugin of the same name.
        if (File.Exists(path))
        {
            logger.LogTrace("Found {Name} plugin", name);

            await using var stream = File.OpenRead(path);
            loaded = TryLoadContainer(name, stream, ignoreEmpty: false, out plugins);
        }
        else if (GetEmbeddedPluginNames().Contains(name) && Assembly.GetExecutingAssembly().GetManifestResourceStream(name) is { } stream)
        {
            logger.LogTrace("Found {Name} embedded plugin", name);

            await using (stream)
                loaded = TryLoadContainer(name, stream, ignoreEmpty: false, out plugins);
        }
        else
        {
            throw new InvalidOperationException($"Plugin container {name} not found");
        }

        if (!loaded)
            throw new InvalidOperationException($"Plugin container {name} is already loaded");

        await LoadPluginsAsync(plugins, cancellationToken);
    }

    public async ValueTask LoadPluginsAsync(IEnumerable<Type> plugins, CancellationToken cancellationToken = default)
    {
        Type[] orderedPlugins = [.. plugins.OrderByDescending(plugin => plugin.IsAssignableTo(typeof(IApiPlugin)))];

        try
        {
            foreach (var plugin in orderedPlugins)
                await LoadPluginInstanceAsync(plugin, cancellationToken);
        }
        finally
        {
            // A container can unload once all its plugins loaded, or failed to, so a failed load doesn't keep it from unloading.
            foreach (var plugin in orderedPlugins)
                FindContainer(plugin)?.MarkLoaded();
        }
    }

    public async ValueTask LoadPluginAsync(Type pluginType, CancellationToken cancellationToken = default)
    {
        try
        {
            await LoadPluginInstanceAsync(pluginType, cancellationToken);
        }
        finally
        {
            FindContainer(pluginType)?.MarkLoaded();
        }
    }

    private async ValueTask LoadPluginInstanceAsync(Type pluginType, CancellationToken cancellationToken)
    {
        var container = FindContainer(pluginType) ?? throw new Exception($"No container found for {pluginType.Name} plugin");

        var plugin = dependencies.CreateInstance<IPlugin>(pluginType, container.CancellationTokenSource.Token);

        logger.LogTrace("Loading {Name} plugin", plugin.Name);

        using (var _ = await _lock.LockAsync(cancellationToken))
        {
            // Add to container before events so listeners of the plugin loading event can resolve configs with that plugin assembly
            container.Add(plugin);

            await events.ThrowAsync(new PluginLoadingEvent(plugin), cancellationToken);
            await events.ThrowAsync(new PluginLoadedEvent(plugin), cancellationToken);
        }

        logger.LogDebug("Loaded {Name} plugin from {AssemblyName} ", plugin.Name, pluginType.Assembly.GetName().Name);
    }

    public async ValueTask UnloadContainersAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Unloading all plugins");
        await _containers.Snapshot
            .Select(reference => reference.Context.Name)
            .WhereNotNull()
            .Select(async name =>
            {
                // A container that refuses to unload is told of, and doesn't keep the others from unloading.
                try
                {
                    await UnloadContainerAsync(name, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Plugin container {ContainerName} couldn't be unloaded", name);
                }
            })
            .WhenAll();
    }

    public async ValueTask UnloadContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        var container = _containers.Snapshot.FirstOrDefault(reference => reference.Context.Name == name) ??
                        throw new Exception($"Plugin container {name} not found");

        if (!container.IsAlive)
            throw new Exception("Plugin context already unloaded");

        // Concurrent unloads of a container find it alike, so only the one that claims it unloads it.
        if (!container.TryBeginUnload())
            throw new InvalidOperationException($"Plugin container {name} is loading or already unloading");

        var count = container.Plugins.Count();

        logger.LogTrace("Unloading {ContextName} {Count} plugin(s)", name, count);

        // The types of the plugin live as long as anything references the plugin, which its instances alone don't tell.
        var pluginType = TrackPluginType(container);

        try
        {
            await ThrowUnloadEventsAsync(container, cancellationToken);

            events.UnregisterListeners(container.Plugins);

            await container.CancellationTokenSource.CancelAsync();
            container.Context.Unload();

            var collectionTime = Stopwatch.GetTimestamp();

            do
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();

                if (IsCollected(container, pluginType))
                    break;

                await Task.Delay(_gcRate, cancellationToken);
            } while (Stopwatch.GetElapsedTime(collectionTime) < _unloadTimeout);

            if (!IsCollected(container, pluginType))
                throw new Exception($"Plugins context {name} refuses to unload. {await DescribeRetentionAsync(pluginType, cancellationToken)}");
        }
        finally
        {
            if (pluginType.IsAllocated)
                pluginType.Free();
        }

        logger.LogInformation("Unloaded {ContextName} {Count} plugin(s)", name, count);

        _containers.RemoveAll(reference => !reference.IsAlive);
    }

    // Its own state machine, so the last plugin it throws the events for isn't kept by the one that waits for the plugin to be collected.
    private async ValueTask ThrowUnloadEventsAsync(WeakPluginContainer container, CancellationToken cancellationToken)
    {
        using var _ = await _lock.LockAsync(cancellationToken);

        foreach (var plugin in container.Plugins)
        {
            logger.LogTrace("Unloading {PluginName} plugin", plugin.Name);
            await events.ThrowAsync(new PluginUnloadingEvent(plugin), cancellationToken);
            await events.ThrowAsync(new PluginUnloadedEvent(plugin), cancellationToken);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GCHandle TrackPluginType(WeakPluginContainer container)
    {
        return container.Plugins.FirstOrDefault() is { } plugin ? GCHandle.Alloc(plugin.GetType(), GCHandleType.Weak) : default;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCollected(WeakPluginContainer container, GCHandle pluginType)
    {
        return pluginType.IsAllocated ? pluginType.Target is null : !container.IsAlive;
    }

    private async ValueTask<string> DescribeRetentionAsync(GCHandle pluginType, CancellationToken cancellationToken)
    {
        if (!pluginType.IsAllocated)
            return "It has no plugins to tell what keeps them alive";

        try
        {
            return $"It is kept alive by:\n{await PluginRetentionInspector.DescribeAsync(pluginType, cancellationToken)}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Couldn't tell what keeps the plugin alive");
            return $"What keeps it alive couldn't be told: {exception.Message}";
        }
    }

    public IEnumerable<Type> LoadContainer(string name, Stream stream, bool ignoreEmpty = false)
    {
        if (TryLoadContainer(name, stream, ignoreEmpty, out var plugins))
            return plugins;

        logger.LogWarning("Plugin container {Name} is already loaded", name);
        return [];
    }

    /// <summary>
    /// Loads a container under a name no other container holds, which concurrent loads of one name take for one of them only.
    /// </summary>
    /// <returns><see langword="false" /> when another container holds the name; otherwise, <see langword="true" />, with the plugins found, if any.</returns>
    private bool TryLoadContainer(string name, Stream stream, bool ignoreEmpty, out IReadOnlyList<Type> plugins)
    {
        logger.LogTrace("Loading {Name} plugins", name);

        PluginAssemblyLoadContext context;

        try
        {
            context = dependencies.CreateInstance<PluginAssemblyLoadContext>(CancellationToken.None, name, stream, _containers);
            plugins = GetPlugins(context.PluginAssembly ?? throw new InvalidOperationException($"Plugin context {name} unloaded as it loaded"));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Plugin {Name} couldn't be loaded", name);
            plugins = [];
            return true;
        }

        if (plugins.Count is 0)
        {
            logger.Log(ignoreEmpty ? LogLevel.Trace : LogLevel.Warning, "Plugin {ContextName} has no IPlugin implementations", context.Name);
            return true;
        }

        if (_containers.TryAdd(new WeakPluginContainer(context), static (existing, added) => existing.Context.Name == added.Context.Name))
            return true;

        context.Unload();
        plugins = [];
        return false;
    }

    public IReadOnlyList<Type> GetPlugins(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;

        logger.LogTrace("Looking for IPlugin interfaces in {Name}", assemblyName);

        try
        {
            var plugins = assembly
                .GetTypes()
                .Where(typeof(IPlugin).IsAssignableFrom)
                .Where(type => !type.IsAbstract);

            return [..plugins];
        }
        catch (ReflectionTypeLoadException exception)
        {
            logger.LogError(exception, "Assembly {Name} cannot be loaded:", assemblyName);
        }

        return [];
    }

    public async ValueTask<IEnumerable<Type>> LoadDirectoryPluginTypesAsync(DirectoryInfo directoryInfo, CancellationToken cancellationToken = default)
    {
        return await directoryInfo
            .GetFiles("*.dll")
            .Select(fileInfo => fileInfo.FullName)
            .Select(async fileName =>
            {
                logger.LogTrace("Found {Name} plugin", Path.GetFileName(fileName));

                await using var stream = File.OpenRead(fileName);
                return LoadContainer(Path.GetFileName(fileName), stream);
            })
            .WhenAll()
            .ContinueWith(task => task.Result.SelectMany(plugins => plugins), cancellationToken);
    }

    [Subscribe]
    public void OnProxyStarting(ProxyStartingEvent @event)
    {
        commands.Register(builder => builder
            .Literal("plugins")
            .Executes(ListContainersAsync)
            .Then(builder => builder
                .Literal("load")
                .Then(builder => builder
                    .Argument(ContainerArgument, Arguments.String())
                    .Suggests(SuggestUnloadedContainers)
                    .Executes(LoadContainerAsync)))
            .Then(builder => builder
                .Literal("unload")
                .Then(builder => builder
                    .Argument(ContainerArgument, Arguments.String())
                    .Suggests(SuggestLoadedContainers)
                    .Executes(UnloadContainerAsync)))
            .Then(builder => builder
                .Literal("reload")
                .Then(builder => builder
                    .Argument(ContainerArgument, Arguments.String())
                    .Suggests(SuggestLoadedContainers)
                    .Executes(ReloadContainerAsync))));
    }

    private async ValueTask<int> ListContainersAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await context.Source.SendMessageAsync("Loaded plugins:", cancellationToken);

        foreach (var name in Containers)
            await context.Source.SendMessageAsync($"- {name}", cancellationToken);

        return 0;
    }

    private async ValueTask<int> LoadContainerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (await FindContainerAsync(context, LoadableContainers, cancellationToken) is not { } name)
            return 1;

        if (Containers.Contains(name))
        {
            await context.Source.SendMessageAsync($"Plugin container {name} is already loaded", cancellationToken);
            return 1;
        }

        logger.LogInformation("Loading {ContainerName} container requested by {Source}", name, context.Source);
        return await TryLoadContainerAsync(context, name, "Loaded", cancellationToken);
    }

    private async ValueTask<int> UnloadContainerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (await FindContainerAsync(context, Containers, cancellationToken) is not { } name)
            return 1;

        logger.LogInformation("Unloading {ContainerName} container requested by {Source}", name, context.Source);

        if (!await TryUnloadContainerAsync(context, name, cancellationToken))
            return 1;

        await context.Source.SendMessageAsync($"Unloaded {name}", cancellationToken);
        return 0;
    }

    private async ValueTask<int> ReloadContainerAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (await FindContainerAsync(context, Containers, cancellationToken) is not { } name)
            return 1;

        logger.LogInformation("Reloading {ContainerName} container requested by {Source}", name, context.Source);

        if (!await TryUnloadContainerAsync(context, name, cancellationToken))
            return 1;

        return await TryLoadContainerAsync(context, name, "Reloaded", cancellationToken);
    }

    private async ValueTask<bool> TryUnloadContainerAsync(CommandContext context, string name, CancellationToken cancellationToken)
    {
        try
        {
            await UnloadContainerAsync(name, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Plugin container {ContainerName} couldn't be unloaded", name);
            await context.Source.SendMessageAsync($"Plugin container {name} couldn't be unloaded: {exception.Message}", cancellationToken);
            return false;
        }
    }

    private async ValueTask<int> TryLoadContainerAsync(CommandContext context, string name, string verb, CancellationToken cancellationToken)
    {
        try
        {
            await LoadContainerAsync(name, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Plugin container {ContainerName} couldn't be loaded", name);
            await context.Source.SendMessageAsync($"Plugin container {name} couldn't be loaded: {exception.Message}", cancellationToken);
            return 1;
        }

        // A container that holds no plugins, such as a library, isn't kept.
        if (!Containers.Contains(name))
        {
            await context.Source.SendMessageAsync($"Plugin container {name} has no plugins to load", cancellationToken);
            return 1;
        }

        await context.Source.SendMessageAsync($"{verb} {name}", cancellationToken);
        return 0;
    }

    /// <summary>
    /// Finds the container a name means: the one named exactly so, ignoring case, or else the only one whose name contains it, such as
    /// <c>essentials</c> for <c>Void.Proxy.Plugins.Essentials.dll</c>. Answers why when none or several match.
    /// </summary>
    private async ValueTask<string?> FindContainerAsync(CommandContext context, IEnumerable<string> containers, CancellationToken cancellationToken)
    {
        var name = context.GetArgument<string>(ContainerArgument);
        var candidates = containers.ToArray();

        if (candidates.FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } exactMatch)
            return exactMatch;

        var matches = candidates.Where(candidate => candidate.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();

        switch (matches.Length)
        {
            case 1:
                return matches[0];
            case 0:
                await context.Source.SendMessageAsync($"No plugin container matches {name}", cancellationToken);
                return null;
            default:
                await context.Source.SendMessageAsync($"{name} matches several plugin containers: {string.Join(", ", matches)}", cancellationToken);
                return null;
        }
    }

    private Suggestions SuggestUnloadedContainers(CommandContext context, SuggestionsBuilder builder)
    {
        return SuggestContainers(builder, LoadableContainers.Except(Containers));
    }

    private Suggestions SuggestLoadedContainers(CommandContext context, SuggestionsBuilder builder)
    {
        return SuggestContainers(builder, Containers);
    }

    // Suggests the containers whose names contain what was typed, as FindContainerAsync matches them.
    private static Suggestions SuggestContainers(SuggestionsBuilder builder, IEnumerable<string> containers)
    {
        return containers
            .Where(name => name.Contains(builder.Remaining, StringComparison.OrdinalIgnoreCase))
            .Aggregate(builder, (suggestionsBuilder, name) => suggestionsBuilder.Suggest(name))
            .Build();
    }

    private WeakPluginContainer? FindContainer(Type pluginType)
    {
        return _containers.Snapshot.FirstOrDefault(reference => reference.Context.PluginAssembly == pluginType.Assembly);
    }

    private static IEnumerable<string> GetEmbeddedPluginNames()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(name => name.Contains(nameof(Plugins)));
    }

    private IEnumerable<string> GetDirectoryPluginNames()
    {
        var directoryInfo = new DirectoryInfo(GetDirectoryPath(DefaultPluginsPath));
        return directoryInfo.Exists ? directoryInfo.GetFiles("*.dll").Select(fileInfo => fileInfo.Name) : [];
    }

    private string GetDirectoryPath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(runOptions.WorkingDirectory, path);
    }
}
