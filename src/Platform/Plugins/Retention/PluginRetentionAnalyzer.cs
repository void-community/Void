using Microsoft.Diagnostics.Runtime;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Finds what keeps an unloaded plugin alive in a dump of the proxy, from a weak handle to one of the plugin's types or to its load context: each
/// chain of references from a root of the heap to the plugin's loader allocator, which lives as long as anything references a type or an object
/// of the plugin, or to the load context, which outlives the plugin's types while anything references it or an object of the plugin.
/// </summary>
internal static class PluginRetentionAnalyzer
{
    private const int MaximumChains = 8;
    private const int MaximumFrames = 12;

    // Threads that wait for work, which tell nothing of what stops a plugin's code from being freed
    private static readonly string[] IdleFrames = ["WaitHandle.", "LowLevelLifoSemaphore", "Monitor.Wait", "Thread.Sleep", "WaitForSocketEvents", "SpinThenBlockingWait", "ManualResetEventSlim.Wait", "SemaphoreSlim.Wait", "PortableThreadPool+GateThread", "WaitSubsystem", "LowLevelMonitor"];
    private const string RuntimeTypeName = "System.RuntimeType";
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The line after the chains that the same chains follow, as the runtime names their frames.</summary>
    public const string FramesHeader = "--- frames ---";

    /// <param name="dumpPath">The dump of the proxy.</param>
    /// <param name="handleAddress">The address of the weak handle to a type of the plugin or to its load context, in the dumped proxy.</param>
    /// <param name="dacPath">The DAC of the dumped runtime, or <see langword="null" /> to find it next to the runtime the dump names.</param>
    /// <param name="output">
    ///     Where the chains are written, each a line of its root and the lines of its references below it, apart by a blank line, and then
    ///     after <see cref="FramesHeader" /> the same chains as the runtime names their frames.
    /// </param>
    public static void Analyze(string dumpPath, ulong handleAddress, string? dacPath, TextWriter output)
    {
        using var dump = DataTarget.LoadDump(dumpPath);
        using var target = new DataTarget(new RuntimeVersionDataReader(dump.DataReader), new DataTargetOptions());

        var clr = target.ClrVersions.FirstOrDefault() ?? throw new InvalidOperationException("The dump holds no .NET runtime");
        using var runtime = (dacPath is null ? clr.CreateRuntime() : clr.CreateRuntime(dacPath)) ?? throw new InvalidOperationException("The runtime of the dump can't be read");
        var heap = runtime.Heap;

        if (runtime.EnumerateHandles().FirstOrDefault(handle => handle.Address == handleAddress) is not { Object: { IsNull: false } handleObject })
        {
            output.WriteLine("The plugin was collected by the time the dump was written");
            return;
        }

        // A load context left of a plugin whose types are collected may still have objects of the plugin, or only itself
        var context = handleObject.Type?.Name is RuntimeTypeName ? 0 : handleObject.Address;
        var loaderAllocatorHandle = context is 0 ? (runtime.GetTypeByMethodTable(handleObject.ReadField<ulong>("m_handle")) ?? throw new InvalidOperationException("The dump holds no type of the plugin")).LoaderAllocatorHandle : FindLoaderAllocatorHandle(runtime, context);
        var loaderAllocator = loaderAllocatorHandle is 0 ? 0 : target.DataReader.ReadPointer(loaderAllocatorHandle);
        var staticFields = new Lazy<Dictionary<ulong, PluginRetentionChain.StaticField>>(() => FindStaticFields(runtime));
        var ownStatics = loaderAllocator is 0 ? [] : FindOwnStatics(heap.GetObject(loaderAllocator));

        using var timeout = new CancellationTokenSource(SearchTimeout);
        var chains = new HashSet<string>();
        var frames = new List<string>();

        // An object of the plugin keeps its loader allocator alive through its type rather than a field, so chains to its objects count too.
        var targets = new List<ulong>();

        if (loaderAllocator is not 0)
        {
            targets.Add(loaderAllocator);
            targets.AddRange(heap.EnumerateObjects().Where(link => link.Type is { IsCollectible: true } type && type.LoaderAllocatorHandle == loaderAllocatorHandle).Select(link => link.Address));
        }

        if (context is not 0)
            targets.Add(context);

        foreach (var (root, path) in new GCRoot(heap, [.. targets]).EnumerateRootPaths(timeout.Token))
        {
            var links = new List<ClrObject>();

            for (var link = path; link is not null; link = link.Next)
                links.Add(heap.GetObject(link.Object));

            // The plugin references itself, as its own static fields and types do, which unloading collects with it, and the runtime keeps the load
            // context of a plugin it hasn't collected yet with a handle of its own. A thread keeps what its stack references, even the plugin's own.
            if (ownStatics.Contains(links[0].Address) || root is not ClrStackRoot && links.SkipLast(1).All(link => IsOfPlugin(link, loaderAllocatorHandle, runtime)))
                continue;

            var (origin, chain) = PluginRetentionChain.Describe(root, links, staticFields.Value, runtime);

            // A chain is reached from several roots at once, such as a static field that a thread uses too, which tell nothing more.
            if (!chains.Add(chain))
                continue;

            if (chains.Count > 1)
                output.WriteLine();

            output.WriteLine($"{origin}:");
            output.WriteLine(chain);
            frames.Add(PluginRetentionChain.DescribeFrames(root, links, staticFields.Value));

            if (chains.Count is MaximumChains)
                break;
        }

        if (chains.Count is 0)
        {
            // The runtime frees the code of a plugin nothing references any more a while after it collected the plugin's loader allocator, once no
            // other collectible code it was bound to is left
            output.WriteLine(loaderAllocator is 0 ? "Nothing references the plugin any more, and the runtime hasn't freed its code yet" : "Nothing outside of the plugin references it");
            DescribeCollectibleModules(runtime, loaderAllocatorHandle, output);
            DescribeThreads(runtime, output);
            return;
        }

        output.WriteLine(FramesHeader);
        output.WriteLine(string.Join("\n\n", frames));
    }

    // The loader allocator keeps the arrays that hold the static fields of the plugin's types, which roots of the heap reach too.
    private static HashSet<ulong> FindOwnStatics(ClrObject loaderAllocator)
    {
        var own = new HashSet<ulong>();

        foreach (var slots in loaderAllocator.EnumerateReferences(carefully: true))
        {
            own.Add(slots.Address);

            foreach (var statics in slots.EnumerateReferences(carefully: true))
                own.Add(statics.Address);
        }

        return own;
    }

    private static bool IsOfPlugin(ClrObject link, ulong loaderAllocatorHandle, ClrRuntime runtime)
    {
        if (link.Type is not { } type)
            return false;

        if (type.IsCollectible && type.LoaderAllocatorHandle == loaderAllocatorHandle)
            return true;

        // A type of the plugin, which its loader allocator keeps
        return type.Name is RuntimeTypeName && runtime.GetTypeByMethodTable(link.ReadField<ulong>("m_handle")) is { IsCollectible: true } runtimeType && runtimeType.LoaderAllocatorHandle == loaderAllocatorHandle;
    }

    // Other collectible code, such as of other plugins or assemblies emitted to be collected, that the plugin's code may still be bound to
    private static void DescribeCollectibleModules(ClrRuntime runtime, ulong loaderAllocatorHandle, TextWriter output)
    {
        var modules = new List<string>();

        foreach (var module in runtime.EnumerateModules())
        {
            var type = module.EnumerateTypeDefToMethodTableMap().Select(map => runtime.GetTypeByMethodTable(map.MethodTable)).FirstOrDefault(type => type is not null);

            if (type is not { IsCollectible: true } && !module.IsDynamic)
                continue;

            var owner = type is { IsCollectible: true } ? $", loader allocator {type.LoaderAllocatorHandle:x}{(type.LoaderAllocatorHandle == loaderAllocatorHandle ? " (the plugin's)" : string.Empty)}" : string.Empty;
            modules.Add($"  {module.AssemblyName ?? module.Name ?? type?.Name ?? "?"}{(module.IsDynamic ? " (emitted)" : string.Empty)}{owner}");
        }

        output.WriteLine();
        output.WriteLine(modules.Count is 0 ? "No collectible code is loaded" : $"Collectible code still loaded:\n{string.Join("\n", modules)}");
    }

    // The runtime doesn't free the code of an unloaded plugin while a thread lists the loaded assemblies, which takes every collectible one alive for
    // as long as it lists them, so what the threads do tells what stops it
    private static void DescribeThreads(ClrRuntime runtime, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Threads as the dump was written:");

        foreach (var thread in runtime.Threads)
        {
            var frames = thread.EnumerateStackTrace().Select(frame => frame.Method).OfType<ClrMethod>().Take(MaximumFrames).Select(method => $"    {method.Signature ?? method.Name}").ToArray();

            if (frames.Length is 0 || IdleFrames.Any(frames[0].Contains))
                continue;

            output.WriteLine($"  Thread {thread.ManagedThreadId}:");

            foreach (var frame in frames)
                output.WriteLine(frame);
        }
    }

    // The types of the plugin tell their load context and loader allocator, which a module's first loaded type tells for all of its types. A context
    // none of whose types is loaded any more has no loader allocator left.
    private static ulong FindLoaderAllocatorHandle(ClrRuntime runtime, ulong context)
    {
        foreach (var module in runtime.EnumerateModules())
        {
            foreach (var (methodTable, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (runtime.GetTypeByMethodTable(methodTable) is not { } type)
                    continue;

                if (type is { IsCollectible: true } && type.AssemblyLoadContextAddress == context)
                    return type.LoaderAllocatorHandle;

                break;
            }
        }

        return 0;
    }

    // Maps the value of each static field of a reference type to the field.
    private static Dictionary<ulong, PluginRetentionChain.StaticField> FindStaticFields(ClrRuntime runtime)
    {
        var values = new Dictionary<ulong, PluginRetentionChain.StaticField>();
        var appDomains = runtime.AppDomains.Concat([runtime.SharedDomain, runtime.SystemDomain]).OfType<ClrAppDomain>().ToArray();

        foreach (var module in runtime.EnumerateModules())
        {
            foreach (var (methodTable, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                if (runtime.GetTypeByMethodTable(methodTable) is not { } type)
                    continue;

                foreach (var field in type.StaticFields)
                {
                    if (!field.IsObjectReference)
                        continue;

                    foreach (var appDomain in appDomains)
                    {
                        if (!field.IsInitialized(appDomain))
                            continue;

                        var value = field.ReadObject(appDomain);

                        if (!value.IsNull)
                            values.TryAdd(value.Address, new PluginRetentionChain.StaticField(type.Name ?? "?", field.Name ?? "?", module.Name));
                    }
                }
            }
        }

        return values;
    }
}
