using System.Diagnostics.CodeAnalysis;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Plugins.Context;

namespace Void.Proxy.Plugins.Containers;

public class WeakPluginContainer
{
    private readonly CopyOnWriteCollection<WeakReference<IPlugin>> _references = new();
    private State _state = State.Loading;

    private enum State
    {
        Loading,
        Loaded,
        Unloading
    }

    [SuppressMessage("Style", "IDE0290:Use primary constructor", Justification = "They keep a strict reference to parameters")]
    public WeakPluginContainer(PluginAssemblyLoadContext context, params IPlugin[] plugins)
    {
        Context = context;

        foreach (var plugin in plugins)
            _references.Add(new WeakReference<IPlugin>(plugin, true));
    }

    public IEnumerable<IPlugin> Plugins => GetReferences();
    public bool IsAlive => _references.Snapshot.All(plugin => plugin.TryGetTarget(out _));
    public PluginAssemblyLoadContext Context { get; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    /// <summary>
    /// Marks the plugins of the container as loaded, after which the container can be unloaded.
    /// </summary>
    public void MarkLoaded()
    {
        Interlocked.CompareExchange(ref _state, State.Loaded, State.Loading);
    }

    /// <summary>
    /// Claims the container for unloading, which one caller can, once its plugins are loaded.
    /// </summary>
    /// <returns><see langword="true" /> for the caller that claimed it; <see langword="false" /> while it loads, or once another caller claimed it.</returns>
    public bool TryBeginUnload()
    {
        return Interlocked.CompareExchange(ref _state, State.Unloading, State.Loaded) is State.Loaded;
    }

    public void Add(IPlugin plugin)
    {
        if (Plugins.Any(existingPlugin => existingPlugin.GetType().Assembly != plugin.GetType().Assembly))
            throw new InvalidOperationException($"Plugin {plugin.Name} is not from the same assembly as the others");

        _references.Add(new WeakReference<IPlugin>(plugin, true));
    }

    private IEnumerable<IPlugin> GetReferences()
    {
        foreach (var reference in _references.Snapshot)
        {
            yield return !reference.TryGetTarget(out var plugin)
                ? throw new ObjectDisposedException(Context.Name)
                : plugin;
        }
    }
}
