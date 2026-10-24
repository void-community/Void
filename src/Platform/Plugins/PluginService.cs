using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using Nito.AsyncEx;
using Nito.Disposables.Internals;
using Void.Proxy.Api;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Extensions;
using Void.Proxy.Plugins.Containers;
using Void.Proxy.Plugins.Context;

namespace Void.Proxy.Plugins;

public class PluginService(ILogger<PluginService> logger, IRunOptions runOptions, IEventService events, IDependencyService dependencies, IConsoleService consoleService, HttpClient httpClient) : IPluginService
{
    public const string DefaultPluginsPath = "plugins";

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
        if (Containers.Contains(name))
            throw new InvalidOperationException($"Plugin container {name} is already loaded");

        IEnumerable<Type> plugins;
        var path = Path.Combine(GetDirectoryPath(DefaultPluginsPath), name);

        // A plugin file takes the place of the embedded plugin of the same name.
        if (File.Exists(path))
        {
            logger.LogTrace("Found {Name} plugin", name);

            await using var stream = File.OpenRead(path);
            plugins = LoadContainer(name, stream);
        }
        else if (GetEmbeddedPluginNames().Contains(name) && Assembly.GetExecutingAssembly().GetManifestResourceStream(name) is { } stream)
        {
            logger.LogTrace("Found {Name} embedded plugin", name);

            await using (stream)
                plugins = LoadContainer(name, stream);
        }
        else
        {
            throw new InvalidOperationException($"Plugin container {name} not found");
        }

        await LoadPluginsAsync(plugins, cancellationToken);
    }

    public async ValueTask LoadPluginsAsync(IEnumerable<Type> plugins, CancellationToken cancellationToken = default)
    {
        plugins = plugins.OrderByDescending(plugin => plugin.IsAssignableTo(typeof(IApiPlugin)));

        foreach (var plugin in plugins)
            await LoadPluginAsync(plugin, cancellationToken);
    }

    public async ValueTask LoadPluginAsync(Type pluginType, CancellationToken cancellationToken = default)
    {
        var container = _containers.Snapshot.FirstOrDefault(reference => reference.Context.PluginAssembly == pluginType.Assembly) ??
                        throw new Exception($"No container found for {pluginType.Name} plugin");

        var plugin = dependencies.CreateInstance<IPlugin>(pluginType, container.CancellationTokenSource.Token);

        logger.LogTrace("Loading {Name} plugin", plugin.Name);

        using (var _ = await _lock.LockAsync(cancellationToken))
        {
            // Add to container before events so listeners of the plugin loading event can resolve configs with that plugin assembly
            container.Add(plugin);

            await events.ThrowAsync(new PluginLoadingEvent(plugin), cancellationToken);
            await events.ThrowAsync(new PluginLoadedEvent(plugin), cancellationToken);
        }

        logger.LogDebug("Loaded {Name} plugin from {AssemblyName} ", plugin.Name, container.Context.PluginAssembly.GetName().Name);
    }

    public async ValueTask UnloadContainersAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Unloading all plugins");
        await _containers.Snapshot
            .Select(reference => reference.Context.Name)
            .WhereNotNull()
            .Select(async name => await UnloadContainerAsync(name, cancellationToken))
            .WhenAll();
    }

    public async ValueTask UnloadContainerAsync(string name, CancellationToken cancellationToken = default)
    {
        var container = _containers.Snapshot.FirstOrDefault(reference => reference.Context.Name == name) ??
                        throw new Exception($"Plugin container {name} not found");

        if (!container.IsAlive)
            throw new Exception("Plugin context already unloaded");

        var count = container.Plugins.Count();

        logger.LogTrace("Unloading {ContextName} {Count} plugin(s)", name, count);

        using (var _ = await _lock.LockAsync(cancellationToken))
        {
            foreach (var plugin in container.Plugins)
            {
                logger.LogTrace("Unloading {PluginName} plugin", plugin.Name);
                await events.ThrowAsync(new PluginUnloadingEvent(plugin), cancellationToken);
                await events.ThrowAsync(new PluginUnloadedEvent(plugin), cancellationToken);
            }
        }

        events.UnregisterListeners(container.Plugins);

        await container.CancellationTokenSource.CancelAsync();
        container.Context.Unload();

        var collectionTime = Stopwatch.GetTimestamp();

        do
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            if (!container.IsAlive)
                break;

            await Task.Delay(_gcRate, cancellationToken);
        } while (Stopwatch.GetElapsedTime(collectionTime) < _unloadTimeout);

        if (container.IsAlive)
            throw new Exception($"Plugins context {name} refuses to unload");

        logger.LogInformation("Unloaded {ContextName} {Count} plugin(s)", name, count);

        _containers.RemoveAll(reference => !reference.IsAlive);
    }

    public IEnumerable<Type> LoadContainer(string name, Stream stream, bool ignoreEmpty = false)
    {
        logger.LogTrace("Loading {Name} plugins", name);

        try
        {
            var context = dependencies.CreateInstance<PluginAssemblyLoadContext>(CancellationToken.None, name, stream, _containers);

            var plugins = GetPlugins(context.PluginAssembly);

            if (plugins.Count is 0)
            {
                logger.Log(ignoreEmpty ? LogLevel.Trace : LogLevel.Warning, "Plugin {ContextName} has no IPlugin implementations", context.Name);
                return plugins;
            }

            _containers.Add(new WeakPluginContainer(context));

            return plugins;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Plugin {Name} couldn't be loaded", name);
            return [];
        }
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
