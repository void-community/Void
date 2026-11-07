using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Tomlet;
using Tomlet.Attributes;
using Tomlet.Exceptions;
using Tomlet.Models;
using Void.Proxy.Api.Configurations.Attributes;
using Void.Proxy.Api.Configurations.Exceptions;
using Void.Proxy.Api.Configurations.Serializer;

namespace Void.Proxy.Configurations.Serializers;

public class ConfigurationTomlSerializer : IConfigurationSerializer
{
    // Tomlet caches a serializer and a deserializer of every type it maps in static dictionaries, which it neither guards nor lets anyone
    // remove from. Every proxy in the process shares them, so one lock guards them all.
    private static readonly Lock TomletLock = new();
    private static readonly Dictionary<Type, Delegate>[] TomletCaches = [GetTomletCache("Serializers"), GetTomletCache("Deserializers")];

    // Held weakly, so forgetting an assembly doesn't keep it alive. A serialization under way as its plugin unloads caches its types again,
    // which are forgotten again once it's done.
    private static readonly ConditionalWeakTable<Assembly, object> ForgottenAssemblies = new();

    private readonly Dictionary<Type, Type> _typeTomlMappedCache = [];
    private readonly TomlSerializerOptions _options = new()
    {
        // Allows record type constructors
        OverrideConstructorValues = true,
        IgnoreInvalidEnumValues = false,
        IgnoreNonPublicMembers = true,
        MaxTableEntriesCountToInline = 5
    };

    /// <summary>
    /// Forgets the types of an assembly, and the types mapped from them, so no cache keeps the assembly alive once its plugin unloads.
    /// </summary>
    public void RemoveAssemblyCache(Assembly assembly)
    {
        lock (TomletLock)
        {
            ForgottenAssemblies.AddOrUpdate(assembly, assembly);
            ForgetTypes();
        }
    }

    public string Serialize<TConfiguration>() where TConfiguration : notnull
    {
        return Serialize<TConfiguration>(default);
    }

    public string Serialize(object configuration)
    {
        if (configuration is Type configurationType)
            return Serialize(configuration: null, configurationType);
        else
            return Serialize(configuration, configuration.GetType());
    }

    public string Serialize<TConfiguration>(TConfiguration? configuration) where TConfiguration : notnull
    {
        return Serialize(configuration, typeof(TConfiguration));
    }

    public string Serialize(object? configuration, Type configurationType)
    {
        TomlDocument document;

        try
        {
            configuration ??= CreateInstanceWithDefaults(configurationType);

            lock (TomletLock)
            {
                document = MapTomlDocument(configuration);

                if (IsForgotten(configuration.GetType()))
                    ForgetTypes();
            }
        }
        catch (TomlException exception)
        {
            throw new InvalidConfigurationException($"Failed to serialize configuration: {exception.Message}");
        }

        return document.SerializedValue;
    }

    public TConfiguration Deserialize<TConfiguration>(string source) where TConfiguration : notnull
    {
        return ConfigurationService.CastConfiguration<TConfiguration>(Deserialize(source, typeof(TConfiguration)));
    }

    public object Deserialize(string source, Type configurationType)
    {
        TomlDocument document;

        try
        {
            document = new TomlParser().Parse(source);
        }
        catch (TomlException exception)
        {
            throw new InvalidConfigurationException($"Failed to parse configuration file: {exception.Message}");
        }

        try
        {
            lock (TomletLock)
            {
                var configuration = TomletMain.To(configurationType, document, _options);

                if (IsForgotten(configurationType))
                    ForgetTypes();

                return configuration;
            }
        }
        catch (TomlException exception)
        {
            throw new InvalidConfigurationException($"Failed to deserialize configuration: {exception.Message}");
        }
    }

    private TomlDocument MapTomlDocument(object configuration)
    {
        var configurationType = configuration.GetType();

        var extendedConfigurationType = MapTomlType(configurationType);
        var extendedConfiguration = CreateInstanceWithDefaults(extendedConfigurationType, configuration);

        var configurationDocument = TomletMain.DocumentFrom(configurationType, configuration, _options);
        var extendedConfigurationDocument = TomletMain.DocumentFrom(extendedConfigurationType, extendedConfiguration, _options);

        SwapTomletConfiguration(configurationDocument, extendedConfigurationDocument);

        return configurationDocument;
    }

    private Type MapTomlType(Type type, ModuleBuilder? moduleBuilder = null, Dictionary<Type, Type>? cache = null)
    {
        if (_typeTomlMappedCache.TryGetValue(type, out var cachedType))
            return cachedType;

        if (moduleBuilder is null)
        {
            var name = $"{type.FullName}TomletMappedByVoidAssembly";
            // Collected once RemoveAssemblyCache forgets the types mapped into it, so mapping again after a plugin reloads leaves nothing behind.
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);

            moduleBuilder = assemblyBuilder.DefineDynamicModule(name);
        }

        var root = cache is null;
        cache ??= [];

        if (cache.TryGetValue(type, out var extendedType))
            return extendedType;

        if (type.IsArray)
        {
            var elementType = type.GetElementType() ?? throw new InvalidOperationException($"Array type {type} does not have an element type.");
            var mappedElementType = MapTomlType(elementType, moduleBuilder, cache);

            return cache[type] = mappedElementType != elementType ? mappedElementType.MakeArrayType() : type;
        }

        if (type.IsGenericType)
        {
            var genericDefinition = type.GetGenericTypeDefinition();
            var genericArguments = type.GetGenericArguments();
            var mappedArguments = genericArguments.Select(argument => MapTomlType(argument, moduleBuilder, cache)).ToArray();

            // If any generic argument was mapped to a different type, rebuild the generic type.
            for (int i = 0; i < genericArguments.Length; i++)
            {
                if (genericArguments[i] == mappedArguments[i])
                    continue;

                return cache[type] = genericDefinition.MakeGenericType(mappedArguments);
            }

            return cache[type] = type;
        }

        if (IsSystemType(type))
            return cache[type] = type;

        var typeName = type.FullName + "TomletMappedByVoidType";

        if (moduleBuilder.GetType(typeName) is { } existingType)
            return cache[type] = existingType;

        var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public);
        var attributesWithValues = new Dictionary<Type, object[]>(2);

        if (root)
        {
            foreach (var attribute in type.GetCustomAttributes())
            {
                if (attribute is not ConfigurationAttribute configurationAttribute)
                    continue;

                if (!string.IsNullOrWhiteSpace(configurationAttribute.InlineComment))
                    attributesWithValues.Add(typeof(TomlInlineCommentAttribute), [configurationAttribute.InlineComment]);

                if (!string.IsNullOrWhiteSpace(configurationAttribute.PrecedingComment))
                    attributesWithValues.Add(typeof(TomlPrecedingCommentAttribute), [configurationAttribute.PrecedingComment]);
            }

            foreach (var (attributeType, attributeValues) in attributesWithValues)
            {
                var attributeConstructor = attributeType.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).FirstOrDefault()
                    ?? throw new InvalidOperationException($"No public constructor found for constructor attribute type {type.Name}");

                typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, attributeValues));
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            attributesWithValues.Clear();

            if (property.GetCustomAttribute<ConfigurationPropertyAttribute>() is { } configurationPropertyAttribute)
            {
                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.Name))
                    attributesWithValues.Add(typeof(TomlPropertyAttribute), [configurationPropertyAttribute.Name]);

                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.InlineComment))
                    attributesWithValues.Add(typeof(TomlInlineCommentAttribute), [configurationPropertyAttribute.InlineComment]);

                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.PrecedingComment))
                    attributesWithValues.Add(typeof(TomlPrecedingCommentAttribute), [configurationPropertyAttribute.PrecedingComment]);
            }

            var propertyBuilder = CreateBackedPropertyBuilder(typeBuilder, property.Name, MapTomlType(property.PropertyType, moduleBuilder, cache));

            foreach (var (attributeType, attributeValues) in attributesWithValues)
            {
                var attributeConstructor = attributeType.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).FirstOrDefault()
                    ?? throw new InvalidOperationException($"No public constructor found for property attribute type {type.Name}");

                propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, attributeValues));
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            attributesWithValues.Clear();

            if (field.GetCustomAttribute<ConfigurationPropertyAttribute>() is { } configurationPropertyAttribute)
            {
                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.Name))
                    attributesWithValues.Add(typeof(TomlPropertyAttribute), [configurationPropertyAttribute.Name]);

                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.InlineComment))
                    attributesWithValues.Add(typeof(TomlInlineCommentAttribute), [configurationPropertyAttribute.InlineComment]);

                if (!string.IsNullOrWhiteSpace(configurationPropertyAttribute.PrecedingComment))
                    attributesWithValues.Add(typeof(TomlPrecedingCommentAttribute), [configurationPropertyAttribute.PrecedingComment]);
            }

            var fieldBuilder = typeBuilder.DefineField(field.Name, MapTomlType(field.FieldType, moduleBuilder, cache), FieldAttributes.Public);

            foreach (var (attributeType, attributeValues) in attributesWithValues)
            {
                var attributeConstructor = attributeType.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).FirstOrDefault()
                    ?? throw new InvalidOperationException($"No public constructor found for field attribute type {type.Name}");

                fieldBuilder.SetCustomAttribute(new CustomAttributeBuilder(attributeConstructor, attributeValues));
            }
        }

        extendedType = typeBuilder.CreateType();
        _typeTomlMappedCache[type] = extendedType;

        return cache[type] = extendedType;
    }

    private static Dictionary<Type, Delegate> GetTomletCache(string name)
    {
        return typeof(TomlSerializationMethods).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Dictionary<Type, Delegate>
            ?? throw new InvalidOperationException($"Tomlet has no {name} cache to remove the types of unloaded plugins from");
    }

    // Removes the types of forgotten assemblies, and the types mapped from them, from every cache. Called under TomletLock.
    private void ForgetTypes()
    {
        foreach (var (type, mappedType) in _typeTomlMappedCache.ToArray())
        {
            if (!IsForgotten(type))
                continue;

            _typeTomlMappedCache.Remove(type);
            ForgottenAssemblies.AddOrUpdate(mappedType.Assembly, mappedType.Assembly);
        }

        foreach (var cache in TomletCaches)
        {
            foreach (var type in cache.Keys.ToArray())
            {
                if (IsForgotten(type))
                    cache.Remove(type);
            }

            // Tomlet registers the serializer of every class it maps under object rather than its type, so the last class mapped stays
            // there, whichever plugin it's from. Tomlet maps object again when it needs to.
            cache.Remove(typeof(object));
        }
    }

    // A type is forgotten when it, its element type or any of its generic arguments is from a forgotten assembly, as List<T> is with T.
    private static bool IsForgotten(Type type)
    {
        if (ForgottenAssemblies.TryGetValue(type.Assembly, out _))
            return true;

        if (type.HasElementType && type.GetElementType() is { } elementType && IsForgotten(elementType))
            return true;

        return type.IsGenericType && type.GetGenericArguments().Any(IsForgotten);
    }

    private static void SwapTomletConfiguration(TomlTable table1, TomlTable table2)
    {
        table1.ForceNoInline = true;
        table2.ForceNoInline = true;

        foreach (var (key, value1) in table1)
        {
            if (!table2.TryGetValue(key, out var value2))
                throw new InvalidOperationException($"Key {key} not found in the second table.");

            var precedingComment1 = value1.Comments.PrecedingComment;
            var inlineComment1 = value1.Comments.InlineComment;

            value1.Comments.PrecedingComment = value2.Comments.PrecedingComment;
            value1.Comments.InlineComment = value2.Comments.InlineComment;

            value2.Comments.PrecedingComment = precedingComment1;
            value2.Comments.InlineComment = inlineComment1;

            if (value1 is TomlTable childTable1 && value2 is TomlTable childTable2)
                SwapTomletConfiguration(childTable1, childTable2);
        }
    }

    private static object CreateInstanceWithDefaults(Type type, object? values = null)
    {
        // Handle arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType() ??
                throw new InvalidOperationException("Unable to determine array element type.");

            Array newArray;
            if (values is Array sourceArray)
            {
                newArray = Array.CreateInstance(elementType, sourceArray.Length);
                for (var i = 0; i < sourceArray.Length; i++)
                {
                    var element = sourceArray.GetValue(i);
                    newArray.SetValue(element switch
                    {
                        { } value when !IsSystemType(element.GetType()) => CreateInstanceWithDefaults(elementType, element),
                        _ => element
                    }, i);
                }
            }
            else
            {
                // No source array provided: create an empty array.
                newArray = Array.CreateInstance(elementType, 0);
            }

            return newArray;
        }

        // Handle generic List<T>
        if (IsList(type))
        {
            // If type is generic, retrieve the generic argument; otherwise use object.
            var elementType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);

            // If the provided type is an interface or abstract, create a concrete List<T> for the target element type.
            var concreteType = (type.IsInterface || type.IsAbstract) ? typeof(List<>).MakeGenericType(elementType) : type;

            var listInstance = (IList)(Activator.CreateInstance(concreteType)
                ?? throw new InvalidOperationException($"Unable to create instance of type {concreteType.FullName}"));

            if (values is IEnumerable sourceEnumerable)
            {
                foreach (var item in sourceEnumerable)
                {
                    listInstance.Add(item switch
                    {
                        { } value when !IsSystemType(item.GetType()) => CreateInstanceWithDefaults(elementType, item),
                        _ => item
                    });
                }
            }

            return listInstance;
        }

        // Handle generic Dictionary<K,V>
        if (IsDictionary(type))
        {
            Type keyType, valueType;

            // If the type is generic, extract the generic arguments.
            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments();
                keyType = genericArgs[0];
                valueType = genericArgs[1];
            }
            else // Fallback for non-generic IDictionary; use Object for key and value types.
            {
                keyType = typeof(object);
                valueType = typeof(object);
            }

            // For interfaces or abstract types, fallback to Dictionary<TKey, TValue>.
            var concreteType = (type.IsInterface || type.IsAbstract) ? typeof(Dictionary<,>).MakeGenericType(keyType, valueType) : type;

            var dictionaryInstance = (IDictionary)(Activator.CreateInstance(concreteType)
                ?? throw new InvalidOperationException($"Unable to create instance of type {concreteType.FullName}"));

            if (values is IDictionary sourceDictionary)
            {
                foreach (DictionaryEntry entry in sourceDictionary)
                {
                    var newKey = !IsSystemType(entry.Key.GetType())
                        ? CreateInstanceWithDefaults(keyType, entry.Key)
                        : entry.Key;

                    var newValue = entry.Value is not null && !IsSystemType(entry.Value.GetType())
                        ? CreateInstanceWithDefaults(valueType, entry.Value)
                        : entry.Value;

                    dictionaryInstance.Add(newKey, newValue);
                }
            }

            return dictionaryInstance;
        }

        // Handle generic IEnumerable<T> types not already covered
        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
        {
            var genericArgs = type.GetGenericArguments();

            if (genericArgs.Length is 1) // Assuming a single-type enumerable
            {
                var elementType = genericArgs[0];
                object collectionInstance;

                // If type is interface or abstract, fallback to List<T>
                if (type.IsInterface || type.IsAbstract)
                {
                    collectionInstance = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))
                        ?? throw new InvalidOperationException($"Unable to create instance of type {type.FullName}");
                }
                else
                {
                    // Try using a parameterless constructor first
                    var ctor = type.GetConstructor(Type.EmptyTypes);
                    collectionInstance = ctor is not null ? ctor.Invoke(null) : Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) ?? throw new InvalidOperationException($"Unable to create instance of type {type.FullName}");
                }

                // Find an "Add" method on the concrete type.
                var addMethod = collectionInstance.GetType().GetMethod("Add", [elementType]);

                if (addMethod is not null && values is IEnumerable sourceEnumerable)
                {
                    foreach (var item in sourceEnumerable)
                    {
                        var element = item != null && !IsSystemType(item.GetType())
                            ? CreateInstanceWithDefaults(elementType, item)
                            : item;

                        addMethod.Invoke(collectionInstance, [element]);
                    }

                    return collectionInstance;
                }
            }
        }

        // If the type has an explicit parameterless constructor, use it.
        var parameterlessConstructor = type.GetConstructors().FirstOrDefault(constructor => constructor.GetParameters().Length is 0);

        var instance = parameterlessConstructor switch
        {
            { } value => value.Invoke(null),
            _ => CreateEmptyInstance(type)
        };

        CopyValues(type, instance, values);
        return instance;

        static object CreateEmptyInstance(Type type)
        {
            if (type.IsInterface)
                throw new InvalidOperationException($"Cannot serialize interface {type.Name}");

            if (type.IsAbstract)
                throw new InvalidOperationException($"Cannot serialize {type.Name}");

            // Assume the "primary" constructor is the one with the most parameters.
            // (Records typically have one primary constructor.)
            var constructor = type.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).FirstOrDefault()
                ?? throw new InvalidOperationException($"No public constructor found for type {type.Name}");

            // Prepare the arguments array by iterating over the parameters.
            var parameters = constructor.GetParameters();
            var args = parameters.Select(parameter =>
            {
                // If a default value is present, use it.
                if (parameter.HasDefaultValue && parameter.DefaultValue is not null)
                    return parameter.DefaultValue;

                // For non-nullable value types, use their default instance.
                if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
                    return Activator.CreateInstance(parameter.ParameterType);

                // For system reference types (or nullable value types), use null.
                if (IsSystemType(parameter.ParameterType))
                    return null;

                return CreateEmptyInstance(parameter.ParameterType);
            });

            return constructor.Invoke([.. args]);
        }

        static void CopyValues(Type type, object instance, object? values)
        {
            // Map fields and properties from "object values" to instantiated type if provided
            var valuesType = values?.GetType();

            foreach (var property in type.GetProperties())
            {
                var sourceProperty = valuesType?.GetProperty(property.Name);

                if (sourceProperty is null)
                    continue;

                var value = sourceProperty.GetValue(values);
                var valueType = value?.GetType();
                var valueInstance = value;

                if (value is not null && valueType is not null && (!IsSystemType(valueType) || IsDictionary(valueType) || IsList(valueType) || valueType.IsArray))
                    valueInstance = CreateInstanceWithDefaults(property.PropertyType, value);

                property.SetValue(instance, valueInstance);
            }

            foreach (var field in type.GetFields())
            {
                var sourceField = valuesType?.GetField(field.Name);

                if (sourceField is null)
                    continue;

                var value = sourceField.GetValue(values);
                var valueType = value?.GetType();
                var valueInstance = value;

                if (value is not null && valueType is not null && (!IsSystemType(valueType) || IsDictionary(valueType) || IsList(valueType) || valueType.IsArray))
                    valueInstance = CreateInstanceWithDefaults(field.FieldType, value);

                field.SetValue(instance, valueInstance);
            }
        }
    }

    public static PropertyBuilder CreateBackedPropertyBuilder(TypeBuilder typeBuilder, string propertyName, Type propertyType)
    {
        var fieldBuilder = typeBuilder.DefineField($"_{propertyName}", propertyType, FieldAttributes.Private);
        var propertyBuilder = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);

        var getMethodBuilder = typeBuilder.DefineMethod($"get_{propertyName}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
        var getILGenerator = getMethodBuilder.GetILGenerator();
        getILGenerator.Emit(OpCodes.Ldarg_0);
        getILGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
        getILGenerator.Emit(OpCodes.Ret);

        var setMethodBuilder = typeBuilder.DefineMethod($"set_{propertyName}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, [propertyType]);
        var setILGenerator = setMethodBuilder.GetILGenerator();
        setILGenerator.Emit(OpCodes.Ldarg_0);
        setILGenerator.Emit(OpCodes.Ldarg_1);
        setILGenerator.Emit(OpCodes.Stfld, fieldBuilder);
        setILGenerator.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getMethodBuilder);
        propertyBuilder.SetSetMethod(setMethodBuilder);

        return propertyBuilder;
    }

    // Updated IsSystemType method to also consider types from dynamic assemblies as system types.
    private static bool IsSystemType(Type type) => type.IsEnum || (type.Namespace?.StartsWith(nameof(System)) ?? true);

    private static bool IsList(Type type)
    {
        // Check if the type is a closed generic that is List<> or IList<>
        if (type.IsGenericType)
        {
            var gen = type.GetGenericTypeDefinition();

            if (gen == typeof(List<>) || gen == typeof(IList<>))
                return true;
        }

        // Also check if it implements the non-generic IList or any IList<> interface.
        return typeof(IList).IsAssignableFrom(type) || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
    }

    private static bool IsDictionary(Type type)
    {
        // Check if the type itself is a closed generic Dictionary<,> or IDictionary<,>
        if (type.IsGenericType)
        {
            var gen = type.GetGenericTypeDefinition();

            if (gen == typeof(Dictionary<,>) || gen == typeof(IDictionary<,>))
                return true;
        }

        // Also check if it implements the non-generic IDictionary or any IDictionary<,> interface.
        return typeof(IDictionary).IsAssignableFrom(type) || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
    }
}
