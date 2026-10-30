using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Finds what keeps an unloaded plugin alive from a snapshot of the heap that the runtime sends over EventPipe, for a runtime that has no
/// createdump or DAC to read its memory with, as on Android. The snapshot tells the objects, their types and which objects reference which, but
/// not through which fields, so the chains tell the types they pass through. The proxy takes the snapshot of itself, which tells the types by
/// their handles, so the proxy names them.
/// </summary>
internal static class PluginRetentionSnapshot
{
    private const int MaximumChains = 8;
    private const int FullCollection = 2;
    private const string StaticFieldRoot = "Static field";
    private const string LoaderAllocatorTypeName = "System.Reflection.LoaderAllocator";
    private const int LoaderAllocatorStaticsDepth = 3;
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromMinutes(1);

    /// <summary>Describes the chains of references that keep the plugin alive, as <see cref="PluginRetentionAnalyzer" /> writes them.</summary>
    /// <param name="pluginType">A weak handle to a type of the plugin.</param>
    public static async ValueTask<string> DescribeAsync(GCHandle pluginType, CancellationToken cancellationToken)
    {
        var heap = await Task.Run(() => Collect(Environment.ProcessId), cancellationToken);
        return Describe(heap, GetAssembly(pluginType));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Assembly GetAssembly(GCHandle pluginType)
    {
        return (pluginType.Target as Type)?.Assembly ?? throw new InvalidOperationException("The plugin was collected by the time its snapshot was taken");
    }

    private static string Describe(Heap heap, Assembly plugin)
    {
        var types = new Dictionary<ulong, Type?>();
        var output = new StringBuilder("This runtime can't read its own memory, so the chains tell the types of the objects they pass through, but not their fields.");
        var chains = new HashSet<string>();

        foreach (var (origin, path) in FindPaths(heap, types, plugin))
        {
            var chain = PluginRetentionChain.DescribeTypes([.. path.Select(address => Describe(ResolveType(types, heap.Nodes[address].Type)))], origin.First);

            if (!chains.Add(chain))
                continue;

            output.Append($"\n\n{origin.Kind}:\n{chain}");

            if (chains.Count is MaximumChains)
                break;
        }

        if (chains.Count is 0)
            output.Append("\n\nNothing outside of the plugin references an object of it. Something references one of its types or its assembly, which this runtime can't tell.");

        return output.ToString();
    }

    private static PluginRetentionChain.TypeLink Describe(Type? type)
    {
        if (type is null)
            return new PluginRetentionChain.TypeLink("?", null, null);

        var assembly = type.Assembly.GetName().Name;
        return new PluginRetentionChain.TypeLink(RuntimeName(type), assembly, type.Assembly.IsCollectible ? assembly : null);
    }

    // The snapshot tells types by their handles, which name them in this process only.
    private static Type? ResolveType(Dictionary<ulong, Type?> types, ulong handle)
    {
        if (types.TryGetValue(handle, out var type))
            return type;

        try
        {
            type = Type.GetTypeFromHandle(RuntimeTypeHandle.FromIntPtr((nint)handle));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            type = null;
        }

        types[handle] = type;
        return type;
    }

    // Names a type as the runtime names it, the way the analyzer of a dump reads the names, which nests a type in its generic declaring type.
    private static string RuntimeName(Type type)
    {
        if (type.IsArray)
            return $"{RuntimeName(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";

        if (type.IsGenericParameter)
            return type.Name;

        var arguments = type.IsGenericType ? type.GetGenericArguments() : [];
        var name = type.Name.Split('`')[0];
        var builder = new StringBuilder();

        if (type.DeclaringType is { } declaringType)
        {
            var declaringArguments = declaringType.IsGenericTypeDefinition ? declaringType.GetGenericArguments().Length : 0;
            var declaring = declaringArguments > 0 && arguments.Length >= declaringArguments && !type.IsGenericTypeDefinition ? declaringType.MakeGenericType(arguments[..declaringArguments]) : declaringType;

            builder.Append(RuntimeName(declaring)).Append('+').Append(name);
            arguments = arguments[Math.Min(declaringArguments, arguments.Length)..];
        }
        else
        {
            builder.Append(type.Namespace is { Length: > 0 } ns ? $"{ns}.{name}" : name);
        }

        if (arguments.Length > 0 && !type.IsGenericTypeDefinition)
            builder.Append('<').AppendJoin(", ", arguments.Select(RuntimeName)).Append('>');

        return builder.ToString();
    }

    // The runtime collects the heap as the session starts, and tells each object it marks.
    private static Heap Collect(int processId)
    {
        var client = new DiagnosticsClient(processId);
        EventPipeProvider[] providers = [new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)ClrTraceEventParser.Keywords.GCHeapSnapshot)];

        using var session = client.StartEventPipeSession(providers, requestRundown: false, circularBufferMB: 1024);
        using var source = new EventPipeEventSource(session.EventStream);
        using var timeout = new Timer(state => ((EventPipeSession)state!).Stop(), session, SnapshotTimeout, Timeout.InfiniteTimeSpan);

        var heap = new Heap();
        var collection = (int?)null;
        var stopping = 0;

        source.Clr.GCStart += data =>
        {
            if (collection is null && data.Depth is FullCollection)
                collection = data.Count;
        };

        // The session stops once the collection that took the snapshot ends, which another thread does while this one reads its events
        source.Clr.GCStop += data =>
        {
            if (data.Count == collection && Interlocked.Exchange(ref stopping, 1) is 0)
                Task.Run(session.Stop);
        };

        // Edges come in the order of the objects they leave from, so an object's first edge follows the edges of the objects before it
        source.Clr.GCBulkNode += data =>
        {
            for (var index = 0; index < data.Count; index++)
            {
                var node = data.Values(index);
                heap.Nodes[node.Address] = new HeapNode(node.TypeID, heap.EdgesTold, (int)node.EdgeCount);
                heap.EdgesTold += (int)node.EdgeCount;
            }
        };

        source.Clr.GCBulkEdge += data =>
        {
            for (var index = 0; index < data.Count; index++)
                heap.Edges.Add(data.Values(index).Target);
        };

        source.Clr.GCBulkRootEdge += data =>
        {
            for (var index = 0; index < data.Count; index++)
            {
                var root = data.Values(index);

                // A weak handle keeps nothing alive
                if (root.GCRootFlag.HasFlag(GCRootFlags.WeakRef))
                    continue;

                heap.Roots.Add(new HeapRoot(root.RootedNodeAddress, root.GCRootKind switch
                {
                    GCRootKind.Stack => "stack of a thread",
                    GCRootKind.Finalizer => "Finalizer queue",
                    GCRootKind.Handle => "Handle",
                    _ => root.GCRootKind.ToString()
                }, null, 0));
            }
        };

        source.Clr.GCBulkRootStaticVar += data =>
        {
            for (var index = 0; index < data.Count; index++)
            {
                var root = data.Values(index);
                heap.Roots.Add(new HeapRoot(root.ObjectID, StaticFieldRoot, root.FieldName, root.TypeID));
            }
        };

        source.Clr.GCBulkRootConditionalWeakTableElementEdge += data =>
        {
            for (var index = 0; index < data.Count; index++)
            {
                var edge = data.Values(index);
                heap.DependentEdges.Add((edge.GCKeyNodeID, edge.GCValueNodeID));
            }
        };

        source.Process();
        return heap;
    }

    // Searches from all roots at once, so each object of the plugin is reached the shortest way, and stops at the first object of the plugin on
    // a way, as the plugin's references among its own objects go when it does.
    private static IEnumerable<((string Kind, string? First) Origin, List<ulong> Path)> FindPaths(Heap heap, Dictionary<ulong, Type?> types, Assembly plugin)
    {
        var parents = new Dictionary<ulong, ulong>();
        var origins = new Dictionary<ulong, (string Kind, string? First)>();
        var queue = new Queue<ulong>();
        var dependents = heap.DependentEdges.ToLookup(edge => edge.Key, edge => edge.Value);

        // Static fields name the root best, so they're searched from first
        foreach (var root in heap.Roots.OrderBy(root => root.Kind is StaticFieldRoot ? 0 : 1))
        {
            if (!heap.Nodes.TryGetValue(root.Address, out var rootNode) || origins.ContainsKey(root.Address))
                continue;

            var first = (string?)null;

            if (root.FieldName is { } fieldName)
            {
                var declaringTypes = FindDeclaringTypes(fieldName, ResolveType(types, root.FieldType) ?? ResolveType(types, rootNode.Type));

                // The plugin's own static fields go with it, and one the plugin declares too may be the plugin's, as the static fields that cache
                // the lambdas of each assembly are
                if (declaringTypes.Any(type => type.Assembly == plugin))
                    continue;

                first = declaringTypes is [var declaringType] ? $"{PluginRetentionChain.ShortName(RuntimeName(declaringType))}.{fieldName}" : $"static {fieldName}";
            }

            origins[root.Address] = (root.Kind, first);
            queue.Enqueue(root.Address);
        }

        while (queue.TryDequeue(out var address))
        {
            var node = heap.Nodes[address];
            var type = ResolveType(types, node.Type);

            if (type?.Assembly == plugin || (type?.FullName is LoaderAllocatorTypeName && IsPluginLoaderAllocator(heap, address, types, plugin)))
            {
                yield return (Trace(address, parents, origins, out var path), path);
                continue;
            }

            // A loader allocator keeps the static fields of its plugin's types, which lead to that plugin's own objects only
            if (type?.FullName is LoaderAllocatorTypeName)
                continue;

            for (var edge = node.FirstEdge; edge < node.FirstEdge + node.EdgeCount && edge < heap.Edges.Count; edge++)
                Visit(heap.Edges[edge], address);

            foreach (var target in dependents[address])
                Visit(target, address);
        }

        void Visit(ulong target, ulong parent)
        {
            if (heap.Nodes.ContainsKey(target) && !origins.ContainsKey(target) && parents.TryAdd(target, parent))
                queue.Enqueue(target);
        }
    }

    // The loader allocator of the plugin, which a reference to a type of the plugin keeps, holds the static fields of the plugin's types a few
    // arrays away, which hold objects of the plugin.
    private static bool IsPluginLoaderAllocator(Heap heap, ulong loaderAllocator, Dictionary<ulong, Type?> types, Assembly plugin)
    {
        var level = new List<ulong> { loaderAllocator };

        for (var depth = 0; depth < LoaderAllocatorStaticsDepth && level.Count > 0; depth++)
        {
            var next = new List<ulong>();

            foreach (var address in level)
            {
                var node = heap.Nodes[address];

                for (var edge = node.FirstEdge; edge < node.FirstEdge + node.EdgeCount && edge < heap.Edges.Count; edge++)
                {
                    if (!heap.Nodes.TryGetValue(heap.Edges[edge], out var target))
                        continue;

                    var targetType = ResolveType(types, target.Type);

                    if (targetType?.Assembly == plugin)
                        return true;

                    if (targetType == typeof(object[]))
                        next.Add(heap.Edges[edge]);
                }
            }

            level = next;
        }

        return false;
    }

    private static (string Kind, string? First) Trace(ulong address, Dictionary<ulong, ulong> parents, Dictionary<ulong, (string Kind, string? First)> origins, out List<ulong> path)
    {
        path = [address];

        while (parents.TryGetValue(path[^1], out var parent))
            path.Add(parent);

        path.Reverse();
        return origins[path[0]];
    }

    // The snapshot tells a static field by its name and its declared type only, which few loaded types declare a static field of.
    private static List<Type> FindDeclaringTypes(string fieldName, Type? fieldType)
    {
        var declaringTypes = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetLoadedTypes(assembly))
            {
                var field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                if (field is not null && !field.FieldType.IsValueType && (fieldType is null || field.FieldType == fieldType || field.FieldType.IsAssignableFrom(fieldType)))
                    declaringTypes.Add(type);
            }
        }

        return declaringTypes;
    }

    private static IEnumerable<Type> GetLoadedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private readonly record struct HeapNode(ulong Type, int FirstEdge, int EdgeCount);

    // A static field is told by its name and its declared type
    private readonly record struct HeapRoot(ulong Address, string Kind, string? FieldName, ulong FieldType);

    private sealed class Heap
    {
        public Dictionary<ulong, HeapNode> Nodes { get; } = [];
        public List<ulong> Edges { get; } = [];
        public int EdgesTold { get; set; }
        public List<HeapRoot> Roots { get; } = [];
        public List<(ulong Key, ulong Value)> DependentEdges { get; } = [];
    }
}
