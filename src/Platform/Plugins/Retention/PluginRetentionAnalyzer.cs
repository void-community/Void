using Microsoft.Diagnostics.Runtime;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Finds what keeps an unloaded plugin alive in a dump of the proxy, from a weak handle to one of the plugin's types: each chain of references
/// from a root of the heap to the plugin's loader allocator, which lives as long as anything references a type or an object of the plugin.
/// </summary>
internal static class PluginRetentionAnalyzer
{
    private const int MaximumChains = 8;
    private const int MaximumLinks = 16;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(1);

    /// <param name="dumpPath">The dump of the proxy.</param>
    /// <param name="typeHandleAddress">The address of the weak handle to a type of the plugin, in the dumped proxy.</param>
    /// <param name="dacPath">The DAC of the dumped runtime, or <see langword="null" /> to find it next to the runtime the dump names.</param>
    /// <param name="output">Where the chains are written, one per line.</param>
    public static void Analyze(string dumpPath, ulong typeHandleAddress, string? dacPath, TextWriter output)
    {
        using var dump = DataTarget.LoadDump(dumpPath);
        using var target = new DataTarget(new RuntimeVersionDataReader(dump.DataReader), new DataTargetOptions());

        var clr = target.ClrVersions.FirstOrDefault() ?? throw new InvalidOperationException("The dump holds no .NET runtime");
        using var runtime = (dacPath is null ? clr.CreateRuntime() : clr.CreateRuntime(dacPath)) ?? throw new InvalidOperationException("The runtime of the dump can't be read");
        var heap = runtime.Heap;

        if (runtime.EnumerateHandles().FirstOrDefault(handle => handle.Address == typeHandleAddress) is not { Object: { IsNull: false } typeObject })
        {
            output.WriteLine("The plugin was collected by the time the dump was written");
            return;
        }

        var pluginType = runtime.GetTypeByMethodTable(typeObject.ReadField<ulong>("m_handle")) ?? throw new InvalidOperationException("The dump holds no type of the plugin");
        var loaderAllocatorHandle = pluginType.LoaderAllocatorHandle;
        var loaderAllocator = target.DataReader.ReadPointer(loaderAllocatorHandle);
        var staticFields = new Lazy<Dictionary<ulong, string>>(() => FindStaticFields(runtime));
        var ownStatics = FindOwnStatics(heap.GetObject(loaderAllocator));

        using var timeout = new CancellationTokenSource(SearchTimeout);
        var chains = new HashSet<string>();

        // An object of the plugin keeps its loader allocator alive through its type rather than a field, so chains to its objects count too.
        ulong[] targets = [loaderAllocator, .. heap.EnumerateObjects().Where(link => link.Type is { IsCollectible: true } type && type.LoaderAllocatorHandle == loaderAllocatorHandle).Select(link => link.Address)];

        foreach (var (root, path) in new GCRoot(heap, targets).EnumerateRootPaths(timeout.Token))
        {
            var links = new List<ClrObject>();

            for (var link = path; link is not null; link = link.Next)
                links.Add(heap.GetObject(link.Object));

            // The plugin references itself, as its own static fields and types do, which unloading collects with it.
            if (ownStatics.Contains(links[0].Address) || links.SkipLast(1).All(link => IsOfPlugin(link, loaderAllocatorHandle, runtime)))
                continue;

            var (origin, steps) = Describe(root, links, staticFields);

            // A chain is reached from several roots at once, such as a static field that a thread uses too, which tell nothing more.
            if (!chains.Add(steps))
                continue;

            output.WriteLine($"{origin}: {steps}");

            if (chains.Count is MaximumChains)
                break;
        }

        if (chains.Count is 0)
            output.WriteLine("Nothing outside of the plugin references it");
    }

    private static (string Origin, string Steps) Describe(ClrRoot root, List<ClrObject> links, Lazy<Dictionary<ulong, string>> staticFields)
    {
        var steps = new List<string>(links.Count);

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            var typeName = link.Type?.Name ?? "?";

            // The array the runtime keeps static fields in references their values, so the step names the static field instead.
            if (index is 0 && links.Count > 1 && staticFields.Value.TryGetValue(links[1].Address, out var staticField))
            {
                steps.Add($"static {staticField}");
                continue;
            }

            if (index + 1 < links.Count && FindField(link, links[index + 1].Address) is { } field)
                steps.Add($"{typeName}.{field}");
            else
                steps.Add(typeName);
        }

        if (steps.Count > MaximumLinks)
            steps = [.. steps.Take(MaximumLinks / 2), "…", .. steps.TakeLast(MaximumLinks / 2)];

        var origin = root switch
        {
            ClrStackRoot { StackFrame: { } frame } => $"stack of {frame.Method?.Signature ?? frame.FrameName ?? "a thread"}",
            _ => root.RootKind.ToString()
        };

        return (origin, string.Join(" → ", steps));
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

    private static string? FindField(ClrObject owner, ulong referenced)
    {
        foreach (var reference in owner.EnumerateReferencesWithFields(carefully: true, considerDependantHandles: true))
        {
            if (reference.Object.Address != referenced)
                continue;

            if (reference.Field is { } field)
                return field.Name;

            // Elements of an array are referenced by index rather than by a field.
            return reference.IsDependentHandle ? "(dependent handle)" : null;
        }

        return null;
    }

    private static bool IsOfPlugin(ClrObject link, ulong loaderAllocatorHandle, ClrRuntime runtime)
    {
        if (link.Type is not { } type)
            return false;

        if (type.IsCollectible && type.LoaderAllocatorHandle == loaderAllocatorHandle)
            return true;

        // A type of the plugin, which its loader allocator keeps
        return type.Name is "System.RuntimeType" && runtime.GetTypeByMethodTable(link.ReadField<ulong>("m_handle")) is { IsCollectible: true } runtimeType && runtimeType.LoaderAllocatorHandle == loaderAllocatorHandle;
    }

    // Maps the value of each static field of a reference type to the field's name.
    private static Dictionary<ulong, string> FindStaticFields(ClrRuntime runtime)
    {
        var values = new Dictionary<ulong, string>();
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
                            values.TryAdd(value.Address, $"{type.Name}.{field.Name}");
                    }
                }
            }
        }

        return values;
    }
}
