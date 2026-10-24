using System.Reflection;
using System.Runtime.Loader;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Containers;
using Void.Proxy.Plugins.Dependencies;

namespace Void.Proxy.Plugins.Context;

public class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly ILogger<PluginAssemblyLoadContext> _logger;
    private readonly ManagedResolver _resolver;

    /// <summary>
    /// The plugin assembly, until the context starts unloading. The runtime keeps an unloading context alive until its assemblies are
    /// collected, so a context that kept its assembly would never unload.
    /// </summary>
    public Assembly? PluginAssembly { get; private set; }

    public PluginAssemblyLoadContext(ILogger<PluginAssemblyLoadContext> logger, IDependencyService dependencies, string name, Stream assemblyStream, CopyOnWriteCollection<WeakPluginContainer> containers) : base(name, true)
    {
        _logger = logger;
        _resolver = dependencies.CreateInstance<ManagedResolver>(default, this, containers);

        PluginAssembly = LoadFromStream(assemblyStream);
        Unloading += context => PluginAssembly = null;
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        _logger.LogTrace("Loading {AssemblyName} assembly into {ContextName} context", assemblyName.Name, Name);

        var assembly = _resolver.Resolve(assemblyName) ??
            throw new FileNotFoundException("Unable to resolve requested dependency");

        // Sorry, but where am I supposed to find your dependency?
        // Throw is mandatory to prevent unwanted search in Default context

        return assembly;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        if (_resolver.ResolveUnmanagedDll(unmanagedDllName) is not { } libraryPath)
            return nint.Zero;

        return LoadUnmanagedDllFromPath(libraryPath);
    }
}
