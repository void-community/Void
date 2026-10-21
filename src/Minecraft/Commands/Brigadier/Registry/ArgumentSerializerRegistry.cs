using System;
using System.Collections.Generic;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Serializers;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Definitions;

namespace Void.Minecraft.Commands.Brigadier.Registry;

/// <summary>Resolves the serializers and protocol identifiers of the command argument types Minecraft defines.</summary>
/// <remarks>
/// It holds only the protocol's own definitions, which never change, so every proxy in a process reads them at once. An argument of any other
/// type is read as an <see cref="UnknownArgumentType"/>, which plugins can inspect and replace in the command tree.
/// </remarks>
public class ArgumentSerializerRegistry
{
    // Filled once below and only read afterwards, which concurrent readers can do safely.
    private static readonly Dictionary<ArgumentSerializerMapping, IArgumentSerializer> MappingToSerializer = [];
    private static readonly Dictionary<Type, IArgumentSerializer> ArgumentTypeToSerializer = [];
    private static readonly Dictionary<Type, ArgumentSerializerMapping> ArgumentTypeToMapping = [];

    static ArgumentSerializerRegistry()
    {
        foreach (var definition in ArgumentParserDefinitions.BrigadierArgumentParserDefinitions)
            Register(definition.Mapping, definition.ArgumentType, definition.Serializer);

        foreach (var definition in ArgumentParserDefinitions.MinecraftArgumentParserDefinitions)
            Register(definition.Mapping, definition.ArgumentType, definition.Serializer);
    }

    private static void Register(ArgumentSerializerMapping mapping, Type? argumentType, IArgumentSerializer serializer)
    {
        MappingToSerializer[mapping] = serializer;

        if (argumentType is null)
            return;

        ArgumentTypeToSerializer[argumentType] = serializer;
        ArgumentTypeToMapping[argumentType] = mapping;
    }

    /// <summary>Reads a parser identifier and its properties.</summary>
    /// <param name="buffer">The source command-tree buffer.</param>
    /// <param name="protocolVersion">The source protocol version.</param>
    /// <returns>A normal argument type or a wrapped passthrough argument.</returns>
    /// <exception cref="ArgumentException">The decoded mapping is not registered.</exception>
    public static IArgumentType Deserialize(ref BufferSpan buffer, ProtocolVersion protocolVersion)
    {
        var mapping = DecodeParserMapping(ref buffer, protocolVersion);

        if (!MappingToSerializer.TryGetValue(mapping, out var serializer))
            throw new ArgumentException($"Unexpected argument type mapping identifier {mapping.Identifier}.");

        var argumentType = serializer.Deserialize(ref buffer, protocolVersion);

        if (argumentType is IPassthroughArgumentValue passthroughArgumentValue)
            argumentType = new PassthroughArgumentType(mapping, passthroughArgumentValue);

        return argumentType;
    }

    /// <summary>Writes an argument parser identifier and its properties.</summary>
    /// <param name="buffer">The destination command-tree buffer.</param>
    /// <param name="argumentType">The normal or passthrough argument type.</param>
    /// <param name="protocolVersion">The target protocol version.</param>
    /// <exception cref="ArgumentException">The runtime argument type is unregistered or has no parser ID for the target version.</exception>
    public static void Serialize(ref BufferSpan buffer, IArgumentType argumentType, ProtocolVersion protocolVersion)
    {
        if (argumentType is UnknownArgumentType unknownArgumentType)
        {
            // The type goes back as the server wrote it, even -1, which servers write for a type they never registered.
            if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
                buffer.WriteVarInt(unknownArgumentType.Mapping.VersionParserIdMapping[protocolVersion]);
            else
                buffer.WriteString(unknownArgumentType.Mapping.Identifier);

            buffer.Write(unknownArgumentType.Properties.Span);
            return;
        }

        if (argumentType is PassthroughArgumentType passthroughArgumentType)
        {
            WriteParserIdentifier(ref buffer, passthroughArgumentType.Mappings, protocolVersion);
            passthroughArgumentType.Value.Serialize(ref buffer, protocolVersion);
            return;
        }

        var argumentTypeRuntimeType = argumentType.GetType();

        if (!ArgumentTypeToSerializer.TryGetValue(argumentTypeRuntimeType, out var serializer) || !ArgumentTypeToMapping.TryGetValue(argumentTypeRuntimeType, out var mapping))
            throw new ArgumentException($"Don't know how to serialize {argumentTypeRuntimeType.FullName}");

        WriteParserIdentifier(ref buffer, mapping, protocolVersion);
        serializer.Serialize(argumentType, ref buffer, protocolVersion);
    }

    /// <summary>Writes a numeric parser ID for 1.19 and newer, or a string identifier for older protocols.</summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="mapping">The parser mapping.</param>
    /// <param name="protocolVersion">The target protocol version.</param>
    /// <exception cref="ArgumentException">No numeric ID exists for a target version requiring one.</exception>
    public static void WriteParserIdentifier(ref BufferSpan buffer, ArgumentSerializerMapping mapping, ProtocolVersion protocolVersion)
    {
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
        {
            if (!mapping.TryGetParserId(protocolVersion, out var parserId))
                throw new ArgumentException($"Argument type mapping {mapping} has no parser ID for protocol version {protocolVersion}.");

            buffer.WriteVarInt(parserId);
        }
        else
        {
            buffer.WriteString(mapping.Identifier);
        }
    }

    /// <summary>Reads and resolves a numeric or string parser identifier.</summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="protocolVersion">The source protocol version.</param>
    /// <returns>The registered mapping.</returns>
    /// <exception cref="ArgumentException">The identifier is unknown for the protocol version.</exception>
    public static ArgumentSerializerMapping DecodeParserMapping(ref BufferSpan buffer, ProtocolVersion protocolVersion)
    {
        if (TryDecodeParserMapping(ref buffer, protocolVersion, out var mapping))
            return mapping;

        throw mapping.TryGetParserId(protocolVersion, out var parserId)
            ? new ArgumentException($"Argument type ID {parserId} unknown for protocol version {protocolVersion}.")
            : new ArgumentException($"Argument type mapping identifier {mapping.Identifier} unknown.");
    }

    /// <summary>Reads a numeric or string parser identifier and resolves it to a registered mapping when there is one.</summary>
    /// <param name="buffer">The source buffer.</param>
    /// <param name="protocolVersion">The source protocol version.</param>
    /// <param name="mapping">The registered mapping, or a mapping that names the unregistered argument type as the buffer did.</param>
    /// <returns><see langword="true"/> when the identifier is registered.</returns>
    public static bool TryDecodeParserMapping(ref BufferSpan buffer, ProtocolVersion protocolVersion, out ArgumentSerializerMapping mapping)
    {
        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
        {
            var protocolSpecificId = buffer.ReadVarInt();

            foreach (var registeredMapping in MappingToSerializer.Keys)
            {
                if (!registeredMapping.TryGetParserId(protocolVersion, out var parserId) || parserId != protocolSpecificId)
                    continue;

                mapping = registeredMapping;
                return true;
            }

            mapping = new ArgumentSerializerMapping($"unknown:{protocolSpecificId}", protocolVersion, protocolSpecificId);
            return false;
        }
        else
        {
            var identifier = buffer.ReadString();

            foreach (var registeredMapping in MappingToSerializer.Keys)
            {
                if (registeredMapping.Identifier != identifier)
                    continue;

                mapping = registeredMapping;
                return true;
            }

            mapping = new ArgumentSerializerMapping(identifier);
            return false;
        }
    }
}
