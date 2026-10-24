using System.Reflection;

namespace Void.Proxy.Api.Plugins;

/// <summary>
///     Manages the lifecycle of proxy plugins, including discovery, loading, and unloading
///     of plugin containers backed by isolated <see cref="System.Runtime.Loader.AssemblyLoadContext" /> instances.
/// </summary>
public interface IPluginService
{
    /// <summary>
    ///     Gets all plugin instances currently active across all loaded containers.
    /// </summary>
    IEnumerable<IPlugin> All { get; }

    /// <summary>
    ///     Gets the names of all currently loaded plugin containers.
    /// </summary>
    IEnumerable<string> Containers { get; }

    /// <summary>
    ///     Gets the names of the plugin containers that <see cref="LoadContainerAsync" /> can load: the <c>.dll</c> files in the
    ///     <c>plugins</c> directory and the plugins embedded in the proxy. Loaded containers are included.
    /// </summary>
    IEnumerable<string> LoadableContainers { get; }

    /// <summary>
    ///     Loads plugins specified via the <c>--plugin</c>/<c>-p</c> command-line arguments
    ///     or the <c>VOID_PLUGINS</c> environment variable.
    ///     Each entry may be a local file path, a directory path, or an HTTP/HTTPS URL.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask LoadEnvironmentPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads plugins embedded as manifest resources in the executing assembly.
    ///     Resources whose names contain <c>Plugins</c> are treated as plugin assemblies.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask LoadEmbeddedPluginsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads all <c>.dll</c> files found in <paramref name="path" /> as plugin containers.
    ///     Creates the directory when it does not already exist.
    /// </summary>
    /// <param name="path">Relative or absolute path to the plugins directory. Defaults to <c>"plugins"</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask LoadDirectoryPluginsAsync(string path = "plugins", CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sequentially loads environment plugins (from command-line arguments and environment variables),
    ///     embedded plugins, and directory plugins from <paramref name="path" />.
    /// </summary>
    /// <param name="path">
    ///     Relative or absolute path to the directory scanned for plugin <c>.dll</c> files. Defaults to
    ///     <c>"plugins"</c>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask LoadPluginsAsync(string path = "plugins", CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads the plugin container with the given name from <see cref="LoadableContainers" /> and loads its plugins.
    ///     A <c>.dll</c> file in the <c>plugins</c> directory is loaded in place of the embedded plugin of the same name.
    /// </summary>
    /// <param name="name">The exact name of the container, such as <c>Void.Proxy.Plugins.Essentials.dll</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     The container is already loaded, or no container has that name. Of concurrent loads of one name, all but one throw.
    /// </exception>
    ValueTask LoadContainerAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Instantiates and registers each of the supplied plugin types as live plugins.
    ///     Types implementing <see cref="IApiPlugin" /> are loaded before all other plugin types.
    /// </summary>
    /// <param name="plugins">The plugin types to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask LoadPluginsAsync(IEnumerable<Type> plugins, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Instantiates a single plugin type within its existing container, fires
    ///     <c>PluginLoadingEvent</c> and <c>PluginLoadedEvent</c>, and registers the plugin.
    /// </summary>
    /// <param name="pluginType">The concrete type implementing <see cref="IPlugin" /> to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="Exception">Thrown when no container is found for <paramref name="pluginType" />'s assembly.</exception>
    ValueTask LoadPluginAsync(Type pluginType, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads an assembly from <paramref name="stream" /> into an isolated load context,
    ///     discovers all non-abstract <see cref="IPlugin" /> implementations, and returns those types.
    /// </summary>
    /// <param name="name">A label used to identify the container in logs and the <see cref="Containers" /> list.</param>
    /// <param name="stream">A stream containing the assembly bytes to load.</param>
    /// <param name="ignoreEmpty">
    ///     When <see langword="true" />, logs a trace-level message instead of a warning
    ///     when the assembly contains no <see cref="IPlugin" /> implementations.
    /// </param>
    /// <returns>The non-abstract types that implement <see cref="IPlugin" /> found in the loaded assembly.</returns>
    IEnumerable<Type> LoadContainer(string name, Stream stream, bool ignoreEmpty = false);

    /// <summary>
    ///     Returns all non-abstract types in <paramref name="assembly" /> that implement <see cref="IPlugin" />.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The discovered <see cref="IPlugin" /> implementation types.</returns>
    IReadOnlyList<Type> GetPlugins(Assembly assembly);

    /// <summary>
    ///     Unloads all active plugin containers, firing unload events for every plugin in each container.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask UnloadContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fires <c>PluginUnloadingEvent</c> and <c>PluginUnloadedEvent</c> for every plugin in the named
    ///     container, cancels the container's cancellation token, initiates assembly unload, and then forces
    ///     garbage collection until the weak reference is collected or a 10-second timeout expires.
    /// </summary>
    /// <param name="name">The name of the container to unload, as returned by <see cref="Containers" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="Exception">
    ///     Thrown when the container is not found, is still loading, is already unloading or unloaded, or refuses to unload within the
    ///     timeout. Of concurrent unloads of one container, all but one throw.
    /// </exception>
    ValueTask UnloadContainerAsync(string name, CancellationToken cancellationToken = default);
}
