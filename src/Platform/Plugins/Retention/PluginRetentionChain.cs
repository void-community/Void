using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Tells a chain of references the way code reads: fields and properties by their names, collections by the keys or indices of their elements,
/// async methods by what awaits them and the locals they hold, lambdas by what they captured, and delegates by what they call. Each line goes a
/// step to the right where the chain passes into another assembly.
/// </summary>
internal static partial class PluginRetentionChain
{
    private const string Indentation = "  ";
    private const int MaximumLineLength = 120;
    private const int MaximumKeyLength = 48;
    private const int MaximumScannedElements = 1 << 20;

    // The final line belongs to the plugin, whatever assembly the runtime puts its loader allocator in.
    private const string PluginAssembly = "\0plugin";

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Boolean"] = "bool",
        ["System.Char"] = "char",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Decimal"] = "decimal",
        ["System.Void"] = "void"
    };

    // Collections whose internals are told as an indexer of the element they hold.
    private static readonly string[] Collections =
    [
        "System.Collections.Generic.List<",
        "System.Collections.Generic.Dictionary<",
        "System.Collections.Generic.HashSet<",
        "System.Collections.Generic.Queue<",
        "System.Collections.Generic.Stack<",
        "System.Collections.Generic.LinkedList<",
        "System.Collections.Generic.SortedDictionary<",
        "System.Collections.Generic.SortedSet<",
        "System.Collections.Generic.SortedList<",
        "System.Collections.Concurrent.ConcurrentDictionary<",
        "System.Collections.Concurrent.ConcurrentQueue<",
        "System.Collections.Concurrent.ConcurrentStack<",
        "System.Collections.Concurrent.ConcurrentBag<"
    ];

    // Namespaces whose types are all internals of the collections they make up.
    private static readonly string[] CollectionNamespaces = ["System.Collections.Immutable.", "DryIoc.ImTools."];

    // Fields collections keep their elements in.
    private static readonly string[] StorageFields = ["_items", "_array", "_entries", "_slots", "_buckets", "_tables", "_head", "_tail", "_root", "_list", "_node", "_nodes"];

    private static readonly string[] KeyFields = ["key", "_key", "Key", "<Key>k__BackingField"];

    /// <summary>Describes the chain from its root to its last link, the plugin or its loader allocator.</summary>
    /// <returns>The root, and the chain as lines below it.</returns>
    public static (string Origin, string Chain) Describe(ClrRoot root, IReadOnlyList<ClrObject> links, IReadOnlyDictionary<ulong, StaticField> staticFields, ClrRuntime runtime)
    {
        var writer = new Writer();
        var index = 0;

        // The array the runtime keeps static fields in references their values, so the chain starts at the static field instead.
        if (links.Count > 1 && staticFields.TryGetValue(links[1].Address, out var staticField))
        {
            writer.Start(staticField.Assembly, $"{ShortName(staticField.TypeName)}.{MemberName(staticField.FieldName)}", links[1].Type);
            index = 1;
        }

        var arrival = (string?)null;

        while (index < links.Count - 1)
        {
            var link = links[index];
            var next = links[index + 1];
            var reference = FindReference(link, next.Address);
            var assembly = link.Type?.Module?.Name;

            // DryIoc wraps a value it swaps at once, which code reads through
            if (link.Type?.Name?.StartsWith("DryIoc.ImTools.Ref<") is true)
            {
                index++;
                continue;
            }

            if (FindCollectionEnd(links, index) is { } end)
            {
                writer.Access(writer.Assembly ?? assembly, link, DescribeElement(links, end, runtime), links[end + 1].Type);
                arrival = null;
                index = end + 1;
                continue;
            }

            if (link.Type is { IsArray: true })
            {
                writer.Access(writer.Assembly ?? assembly, link, DescribeArrayElement(link, next.Address), next.Type);
            }
            else if (link.IsDelegate)
            {
                DescribeDelegate(writer, link, next, reference, runtime);
            }
            else if (GetStateMachineType(link) is { } stateMachineType)
            {
                var method = DescribeStateMachine(stateMachineType.Name ?? "?");

                if (reference?.Field?.Name is "StateMachine" && reference.Value.InnerField is { Field: { } local })
                {
                    writer.Line(stateMachineType.Module?.Name, $"{arrival}{method}, which holds {MemberName(local.Name ?? "?")}", next.Type);
                    writer.Receiver = MemberName(local.Name ?? "?");
                }
                else if (reference?.Field?.Name is "StateMachine")
                {
                    // A state machine of a debug build is an object of its own, which tells what it holds
                    index++;
                    continue;
                }
                else if (reference?.Field?.Name is "m_continuationObject")
                {
                    writer.Line(stateMachineType.Module?.Name, $"{arrival}{method}");
                }
                else
                {
                    writer.Line(stateMachineType.Module?.Name, $"{arrival}{method}, through its {MemberName(reference?.Field?.Name ?? "?")}", next.Type);
                }
            }
            else if (IsStateMachine(link.Type?.Name))
            {
                var local = MemberName(reference?.Field?.Name ?? "?");
                writer.Line(assembly, $"{arrival}{DescribeStateMachine(link.Type!.Name!)}, which holds {local}", next.Type);
                writer.Receiver = local;
            }
            else if (IsTask(link.Type))
            {
                // The continuation a task runs is told by the line after it
                if (reference?.Field?.Name is "m_continuationObject" or null)
                    writer.Line(assembly, $"{arrival}{DescribeTask(link.Type!.Name ?? "?")}");
                else
                    writer.Line(assembly, $"{arrival}{DescribeTask(link.Type!.Name ?? "?")}, through its {MemberName(reference.Value.Field.Name ?? "?")}", next.Type);
            }
            else if (link.Type?.Name is { } typeName && DisplayClassPattern().IsMatch(typeName))
            {
                var captured = MemberName(reference?.Field?.Name ?? "?");
                writer.Line(assembly, $"lambda in {DescribeDisplayClass(link.Type)}, which captured {captured}", next.Type);
                writer.Receiver = captured;
            }
            else
            {
                writer.Access(assembly, link, DescribeField(reference), next.Type);
            }

            // A continuation awaits the task that references it, as an async method awaits the one it calls.
            arrival = reference?.Field?.Name is { } field && (field.Contains("continuation", StringComparison.OrdinalIgnoreCase) || field.Contains("promise", StringComparison.OrdinalIgnoreCase)) ? "awaited by " : null;
            index++;
        }

        var last = links[^1];
        writer.Line(PluginAssembly, last.Type?.Name is "System.Reflection.LoaderAllocator" ? "the plugin's loader allocator" : TypeNote(last.Type));

        return (DescribeRoot(root), writer.ToString());
    }

    /// <summary>Describes the chain as the runtime names its frames: each object's type and the field that references the next, one per line.</summary>
    public static string DescribeFrames(ClrRoot root, IReadOnlyList<ClrObject> links, IReadOnlyDictionary<ulong, StaticField> staticFields)
    {
        var builder = new StringBuilder(root is ClrStackRoot { StackFrame: { } frame } ? $"stack of {frame.Method?.Signature ?? frame.FrameName ?? "a thread"}:" : $"{root.RootKind}:");

        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            builder.Append('\n').Append(Indentation);

            if (index is 0 && links.Count > 1 && staticFields.TryGetValue(links[1].Address, out var staticField))
            {
                builder.Append($"static {staticField.TypeName}.{staticField.FieldName}");
                continue;
            }

            builder.Append(link.Type?.Name ?? "?");

            if (index + 1 >= links.Count || FindReference(link, links[index + 1].Address) is not { } reference)
                continue;

            if (reference.Field is { } field)
                builder.Append('.').Append(field.Name).Append(reference.InnerField is { Field: { } inner } ? $".{inner.Name}" : string.Empty);
            else if (reference.IsArrayElement)
                builder.Append($"[{FindArrayElement(link, links[index + 1].Address).Index}]");
            else if (reference.IsDependentHandle)
                builder.Append(" (dependent handle)");
        }

        return builder.ToString();
    }

    private static string DescribeRoot(ClrRoot root)
    {
        return root switch
        {
            ClrStackRoot { StackFrame: { Method: { } method } } => $"stack of {DescribeMethod(method)}",
            ClrStackRoot { StackFrame: { } frame } => $"stack of {frame.FrameName ?? "a thread"}",
            _ => root.RootKind.ToString()
        };
    }

    private static void DescribeDelegate(Writer writer, ClrObject link, ClrObject next, ClrReference? reference, ClrRuntime runtime)
    {
        if (reference?.Field?.Name is not "_target")
        {
            writer.Access(link.Type?.Module?.Name, link, DescribeField(reference), next.Type);
            return;
        }

        // A lambda's captures tell what it captured and an async method's box what awaits it, which says more than the method that runs them
        if (link.AsDelegate().GetDelegateTarget()?.Method is not { } method || GetStateMachineType(next) is not null || DisplayClassPattern().IsMatch(next.Type?.Name ?? string.Empty))
            return;

        writer.Line(method.Type?.Module?.Name, $"calls {DescribeMethod(method)}", next.Type);
    }

    // The last internal of the collection that the link starts, whose next link is the element it holds.
    private static int? FindCollectionEnd(IReadOnlyList<ClrObject> links, int index)
    {
        if (links[index].Type?.Name is not { } name || GetCollectionFamily(name) is not { } family)
            return null;

        var end = index;

        while (end + 2 < links.Count && IsCollectionPart(links[end + 1].Type?.Name, family, FindReference(links[end], links[end + 1].Address)?.Field?.Name))
            end++;

        return end;
    }

    private static string? GetCollectionFamily(string name)
    {
        if (CollectionNamespaces.FirstOrDefault(name.StartsWith) is { } collectionNamespace)
            return collectionNamespace;

        // The collection itself rather than a type nested in it, though its generic arguments may be nested types
        return Collections.Any(name.StartsWith) && !name.EndsWith(']') && !name[(name.LastIndexOf('>') + 1)..].Contains('+') ? name : null;
    }

    private static bool IsCollectionPart(string? name, string family, string? field)
    {
        if (name is null)
            return false;

        if (name.StartsWith(family) && (family.EndsWith('.') || name.Length == family.Length || name[family.Length] is '+' or '['))
            return true;

        return name.EndsWith("]") && field is not null && StorageFields.Contains(field);
    }

    // Tells the element by its key where the collection keeps one, or by its index, as the collection's indexer reads it.
    private static string DescribeElement(IReadOnlyList<ClrObject> links, int end, ClrRuntime runtime)
    {
        var holder = links[end];
        var element = links[end + 1];

        if (holder.Type is { IsArray: true })
        {
            var (index, field) = FindArrayElement(holder, element.Address);

            if (index < 0)
                return "[…]";

            if (field is not null && holder.Type.ComponentType is { } entryType)
            {
                var entryAddress = holder.Type.GetArrayElementAddress(holder.Address, index);

                if (KeyFields.Contains(field.Name))
                    return $".Keys[{DescribeKey(element, runtime) ?? "…"}]";

                if (KeyFields.Select(entryType.GetFieldByName).FirstOrDefault(key => key is not null) is { } keyField && DescribeKey(keyField, entryAddress, interior: true, runtime) is { } key)
                    return $"[{key}]";
            }

            return $"[{index}]";
        }

        if (FindReference(holder, element.Address)?.Field is { } elementField && KeyFields.Contains(elementField.Name))
            return $".Keys[{DescribeKey(element, runtime) ?? "…"}]";

        if (KeyFields.Select(name => holder.Type?.GetFieldByName(name)).FirstOrDefault(key => key is not null) is { } holderKey && DescribeKey(holderKey, holder.Address, interior: false, runtime) is { } holderKeyText)
            return $"[{holderKeyText}]";

        // A list keeps its elements in an array of its own
        for (var index = end - 1; index >= 0 && links[index].Type is { } type && (type.IsArray || GetCollectionFamily(type.Name ?? string.Empty) is not null); index--)
        {
            if (type.IsArray && FindArrayElement(links[index], links[index + 1].Address) is ({ } arrayIndex and >= 0, null))
                return $"[{arrayIndex}]";
        }

        return "[…]";
    }

    private static string DescribeArrayElement(ClrObject array, ulong element)
    {
        var (index, field) = FindArrayElement(array, element);

        if (index < 0)
            return "[…]";

        return field is null ? $"[{index}]" : $"[{index}].{MemberName(field.Name ?? "?")}";
    }

    // The index of the element, and the field of the struct at that index that references it, for an array of structs.
    private static (int Index, ClrInstanceField? Field) FindArrayElement(ClrObject array, ulong element)
    {
        if (array.Type is not { ComponentType: var componentType } arrayType)
            return (-1, null);

        var values = array.AsArray();
        var length = Math.Min(values.Length, MaximumScannedElements);

        for (var index = 0; index < length; index++)
        {
            if (componentType is null || componentType.IsObjectReference)
            {
                if (values.GetObjectValue(index).Address == element)
                    return (index, null);

                continue;
            }

            var entryAddress = arrayType.GetArrayElementAddress(array.Address, index);

            foreach (var field in componentType.Fields)
            {
                if (field.IsObjectReference && field.ReadObject(entryAddress, interior: true).Address == element)
                    return (index, field);
            }
        }

        return (-1, null);
    }

    private static string? DescribeKey(ClrInstanceField field, ulong address, bool interior, ClrRuntime runtime)
    {
        if (field.IsObjectReference)
            return DescribeKey(field.ReadObject(address, interior), runtime);

        if (field.Type is not { } type)
            return null;

        object? value = type.ElementType switch
        {
            ClrElementType.Boolean => field.Read<bool>(address, interior),
            ClrElementType.Char => field.Read<char>(address, interior),
            ClrElementType.Int8 => field.Read<sbyte>(address, interior),
            ClrElementType.UInt8 => field.Read<byte>(address, interior),
            ClrElementType.Int16 => field.Read<short>(address, interior),
            ClrElementType.UInt16 => field.Read<ushort>(address, interior),
            ClrElementType.Int32 => field.Read<int>(address, interior),
            ClrElementType.UInt32 => field.Read<uint>(address, interior),
            ClrElementType.Int64 => field.Read<long>(address, interior),
            ClrElementType.UInt64 => field.Read<ulong>(address, interior),
            _ => null
        };

        if (value is null)
            return null;

        // An enum is told by the name of its value
        if (type.IsEnum && type.AsEnum().EnumerateValues().FirstOrDefault(entry => Equals(Convert.ToInt64(entry.Item2), Convert.ToInt64(value))) is { Item1: { } name })
            return $"{ShortName(type.Name ?? "?")}.{name}";

        return value is char character ? $"'{character}'" : value is bool boolean ? (boolean ? "true" : "false") : value.ToString();
    }

    private static string? DescribeKey(ClrObject key, ClrRuntime runtime)
    {
        if (key.IsNull || key.Type is not { } type)
            return null;

        if (type.IsString)
        {
            var text = key.AsString(MaximumKeyLength + 1) ?? string.Empty;
            return text.Length > MaximumKeyLength ? $"\"{text[..MaximumKeyLength]}…\"" : $"\"{text}\"";
        }

        if (type.Name is "System.RuntimeType")
            return $"typeof({ShortName(key.AsRuntimeType()?.Name ?? "?")})";

        if (type.Name is "System.Reflection.RuntimeAssembly")
            return DescribeAssembly(key, runtime);

        return null;
    }

    private static string? DescribeAssembly(ClrObject assembly, ClrRuntime runtime)
    {
        if (assembly.TryReadStringField("m_fullname", null, out var fullName) && fullName is not null)
            return fullName.Split(',')[0];

        var address = assembly.ReadField<ulong>("m_assembly");
        var module = runtime.EnumerateModules().FirstOrDefault(module => module.AssemblyAddress == address);

        return module?.Name is { Length: > 0 } name ? Path.GetFileNameWithoutExtension(name) : null;
    }

    private static ClrReference? FindReference(ClrObject owner, ulong referenced)
    {
        foreach (var reference in owner.EnumerateReferencesWithFields(carefully: true, considerDependantHandles: true))
        {
            if (reference.Object.Address == referenced)
                return reference;
        }

        return null;
    }

    private static string DescribeField(ClrReference? reference)
    {
        if (reference is not { } found)
            return ".?";

        if (found.Field is not { } field)
            return found.IsDependentHandle ? " (through a dependent handle)" : ".?";

        // An immutable array is read as the array it wraps
        if (found.InnerField is { Field: { } inner } && field.Type?.Name?.StartsWith("System.Collections.Immutable.ImmutableArray<") is not true)
            return $".{MemberName(field.Name ?? "?")}.{MemberName(inner.Name ?? "?")}";

        return $".{MemberName(field.Name ?? "?")}";
    }

    // Names a field as code names it: a property by its name, a parameter of a primary constructor or a hoisted local by theirs.
    private static string MemberName(string name)
    {
        if (name is "<>4__this")
            return "this";

        if (CompilerMemberPattern().Match(name) is { Success: true } match)
            return match.Groups["name"].Value;

        if (AwaiterPattern().IsMatch(name))
            return "an awaiter";

        return name;
    }

    private static ClrType? GetStateMachineType(ClrObject link)
    {
        if (link.Type?.Name?.Contains("+AsyncStateMachineBox<") is not true || link.Type.GetFieldByName("StateMachine") is not { } field)
            return null;

        // A box of shared generic code names its state machine __Canon, which the state machine itself names
        return field.IsObjectReference ? link.ReadObjectField(field).Type ?? field.Type : field.Type;
    }

    private static bool IsStateMachine(string? name)
    {
        return name is not null && (StateMachinePattern().IsMatch(name) || LambdaStateMachinePattern().IsMatch(name) || LocalFunctionStateMachinePattern().IsMatch(name));
    }

    private static bool IsTask(ClrType? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name is "System.Threading.Tasks.Task")
                return true;
        }

        return false;
    }

    private static string DescribeTask(string name)
    {
        if (name.StartsWith("System.Threading.Tasks.Task+DelayPromise"))
            return "Task.Delay()";

        if (name.Contains("WhenAllPromise"))
            return "Task.WhenAll()";

        if (name.Contains("WhenAnyPromise"))
            return "Task.WhenAny()";

        if (name.Contains("+CancellationPromise"))
            return "Task.WaitAsync()";

        if (name.Contains("+ValueTaskSourceAsTask"))
            return "ValueTask.AsTask()";

        return ShortName(name);
    }

    // Names the method a state machine runs, from the name the compiler gives it.
    private static string DescribeStateMachine(string name)
    {
        if (LambdaStateMachinePattern().Match(name) is { Success: true } lambda)
            return $"lambda in {ShortName(StripClosures(lambda.Groups["outer"].Value))}.{lambda.Groups["method"].Value}()";

        if (LocalFunctionStateMachinePattern().Match(name) is { Success: true } localFunction)
            return $"{localFunction.Groups["function"].Value}() in {ShortName(StripClosures(localFunction.Groups["outer"].Value))}.{localFunction.Groups["method"].Value}()";

        if (StateMachinePattern().Match(name) is { Success: true } method)
            return $"{ShortName(StripClosures(method.Groups["outer"].Value))}.{method.Groups["method"].Value}()";

        return ShortName(name);
    }

    private static string DescribeMethod(ClrMethod method)
    {
        var typeName = method.Type?.Name ?? "?";

        if (typeName.Contains("+AsyncStateMachineBox<"))
            return "an async method";

        if (IsStateMachine(typeName))
            return DescribeStateMachine(typeName);

        if (LambdaPattern().Match(method.Name ?? string.Empty) is { Success: true } lambda)
            return $"a lambda in {ShortName(StripClosures(typeName))}.{lambda.Groups["method"].Value}()";

        if (LocalFunctionPattern().Match(method.Name ?? string.Empty) is { Success: true } localFunction)
            return $"{localFunction.Groups["function"].Value}() in {ShortName(StripClosures(typeName))}.{localFunction.Groups["method"].Value}()";

        return $"{ShortName(typeName)}.{method.Name}()";
    }

    // The compiler names the class of a lambda's captures by the order of the method it's in, and its lambdas by the method's name.
    private static string DescribeDisplayClass(ClrType type)
    {
        var method = type.Methods.Select(method => LambdaPattern().Match(method.Name ?? string.Empty)).FirstOrDefault(match => match.Success)?.Groups["method"].Value;
        return $"{ShortName(StripClosures(type.Name ?? "?"))}.{method ?? "?"}()";
    }

    private static string StripClosures(string name)
    {
        return ClosurePattern().Replace(name, string.Empty);
    }

    /// <summary>Names a type of the runtime as C# does, without namespaces.</summary>
    public static string ShortName(string name)
    {
        var builder = new StringBuilder();
        var position = 0;

        WriteTypeName(name, ref position, builder);

        if (position < name.Length)
            builder.Append(name, position, name.Length - position);

        return builder.ToString();
    }

    // Writes the type at the position until the end of the generic argument it is.
    private static void WriteTypeName(string name, ref int position, StringBuilder builder)
    {
        var segment = builder.Length;
        var qualified = new StringBuilder();
        var outermost = true;

        while (position < name.Length && name[position] is not (',' or '>'))
        {
            var character = name[position];

            switch (character)
            {
                // A name the compiler gives, such as <Method>d__1, which a segment starts with
                case '<' when builder.Length == segment:
                    {
                        var end = FindClosingBracket(name, position);

                        while (end < name.Length && (char.IsLetterOrDigit(name[end]) || name[end] is '_' or '|' or '$' or '`'))
                            end++;

                        builder.Append(name, position, end - position);
                        qualified.Append(name, position, end - position);
                        position = end;
                        break;
                    }
                case '<':
                    {
                        ApplyAlias(builder, segment, qualified, outermost);
                        outermost = false;
                        builder.Append('<');
                        position++;

                        for (var first = true; first || (position < name.Length && name[position - 1] is ','); first = false)
                        {
                            while (position < name.Length && name[position] is ' ')
                                position++;

                            if (!first)
                                builder.Append(", ");

                            WriteTypeName(name, ref position, builder);
                            position++;
                        }

                        builder.Append('>');
                        break;
                    }
                case '.':
                    builder.Length = segment;
                    qualified.Append(character);
                    position++;
                    break;
                case '+':
                    ApplyAlias(builder, segment, qualified, outermost);
                    outermost = false;
                    builder.Append('.');
                    position++;
                    segment = builder.Length;
                    qualified.Clear();
                    break;
                case '[':
                    {
                        ApplyAlias(builder, segment, qualified, outermost);
                        outermost = false;

                        var end = name.IndexOf(']', position);
                        end = end < 0 ? name.Length : end + 1;

                        builder.Append(name, position, end - position);
                        position = end;
                        break;
                    }
                default:
                    builder.Append(character);
                    qualified.Append(character);
                    position++;
                    break;
            }
        }

        ApplyAlias(builder, segment, qualified, outermost);
    }

    private static void ApplyAlias(StringBuilder builder, int segment, StringBuilder qualified, bool outermost)
    {
        if (!outermost || !Aliases.TryGetValue(qualified.ToString(), out var alias))
            return;

        builder.Length = segment;
        builder.Append(alias);
        qualified.Clear();
    }

    private static int FindClosingBracket(string name, int position)
    {
        var depth = 0;

        for (var index = position; index < name.Length; index++)
        {
            if (name[index] is '<')
                depth++;
            else if (name[index] is '>' && --depth is 0)
                return index + 1;
        }

        return name.Length;
    }

    // Names a type, and the plugin it's of, as the chain passes through plugins other than the one that refuses to unload too.
    private static string TypeNote(ClrType? type)
    {
        var name = ShortName(type?.Name ?? "?");

        if (type is not { IsCollectible: true, Module: { } module })
            return name;

        var plugin = module.Name is { Length: > 0 } fileName ? Path.GetFileNameWithoutExtension(fileName) : module.AssemblyName;
        return plugin is { Length: > 0 } ? $"{name} of {plugin}" : name;
    }

    private static string VariableName(ClrObject owner)
    {
        if (owner.Type?.Name is "System.RuntimeType")
            return $"typeof({ShortName(owner.AsRuntimeType()?.Name ?? "?")})";

        var name = GenericArgumentsPattern().Replace(ShortName(owner.Type?.Name ?? "?"), string.Empty).TrimEnd('[', ']', ',');
        name = name[(name.LastIndexOf('.') + 1)..];

        return name.Length is 0 || !char.IsLetter(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>A static field of a reference type, with the assembly of the type it's declared in.</summary>
    public readonly record struct StaticField(string TypeName, string FieldName, string? Assembly);

    // Lines of a chain, each with the assembly of what it tells, which an access joins while the chain stays in that assembly, and the types
    // of the objects the line passes through, which its code names leave out.
    private sealed class Writer
    {
        private readonly List<(string? Assembly, StringBuilder Text, List<string> Types)> _lines = [];
        private bool _open;

        // What the next line calls the object it starts with, as a lambda calls what it captured.
        public string? Receiver { get; set; }

        public string? Assembly => _open ? _lines[^1].Assembly : null;

        public void Start(string? assembly, string text, ClrType? reached)
        {
            _lines.Add((assembly, new StringBuilder(text), [TypeNote(reached)]));
            _open = true;
        }

        public void Access(string? assembly, ClrObject owner, string access, ClrType? reached)
        {
            if (_open && _lines[^1].Assembly == assembly)
            {
                if (_lines[^1].Text.Length + access.Length <= MaximumLineLength)
                {
                    _lines[^1].Text.Append(access);
                    _lines[^1].Types.Add(TypeNote(reached));
                }
                else
                {
                    _lines.Add((assembly, new StringBuilder(access), [TypeNote(reached)]));
                }

                return;
            }

            _lines.Add((assembly, new StringBuilder((Receiver ?? VariableName(owner)) + access), [TypeNote(owner.Type), TypeNote(reached)]));
            _open = true;
            Receiver = null;
        }

        public void Line(string? assembly, string text, ClrType? reached = null)
        {
            _lines.Add((assembly, new StringBuilder(text), reached is null ? [] : [TypeNote(reached)]));
            _open = false;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            var depth = 1;

            for (var index = 0; index < _lines.Count; index++)
            {
                var (assembly, text, types) = _lines[index];

                if (index > 0 && assembly != _lines[index - 1].Assembly)
                    depth++;

                if (index > 0)
                    builder.Append('\n');

                builder.Insert(builder.Length, Indentation, depth).Append(text);

                // A type the line passes through again, as the nodes of a list are, is told once
                var distinct = types.Where((type, position) => position is 0 || type != types[position - 1]).ToArray();

                if (distinct.Length > 0)
                    builder.Append("  // ").AppendJoin(" → ", distinct);
            }

            return builder.ToString();
        }
    }

    [GeneratedRegex(@"^(?<outer>[^<]+(<.+>)?[^<]*?)\+<(?<method>[^<>]+|<[^<>]+>\$)>d__\d+$")]
    private static partial Regex StateMachinePattern();

    [GeneratedRegex(@"^(?<outer>.+)\+<<(?<method>[^<>]+)>b__[\w]+>d(__\d+)?$")]
    private static partial Regex LambdaStateMachinePattern();

    [GeneratedRegex(@"^(?<outer>.+)\+<<(?<method>[^<>]+)>g__(?<function>[^|]+)\|[\w]+>d(__\d+)?$")]
    private static partial Regex LocalFunctionStateMachinePattern();

    [GeneratedRegex(@"^<(?<method>[^<>]+)>b__")]
    private static partial Regex LambdaPattern();

    [GeneratedRegex(@"^<(?<method>[^<>]+)>g__(?<function>[^|]+)\|")]
    private static partial Regex LocalFunctionPattern();

    [GeneratedRegex(@"\+<>c__DisplayClass[\w]+$")]
    private static partial Regex DisplayClassPattern();

    [GeneratedRegex(@"(\+<>c(__DisplayClass[\w]+)?)+$")]
    private static partial Regex ClosurePattern();

    [GeneratedRegex(@"^<(?<name>[^<>]+)>(k__BackingField|P|5__\d+)$")]
    private static partial Regex CompilerMemberPattern();

    [GeneratedRegex(@"^<>u__\d+$")]
    private static partial Regex AwaiterPattern();

    [GeneratedRegex(@"<[^<>]*(<[^<>]*>[^<>]*)*>")]
    private static partial Regex GenericArgumentsPattern();
}
