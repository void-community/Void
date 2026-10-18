using System.Collections.Frozen;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Serializers;
using Void.Minecraft.Network;

namespace Void.Proxy.Plugins.ModsSupport.CrossStitch;

public class CrossStitchModArgumentSerializer : IArgumentSerializer
{
    private static readonly FrozenDictionary<ProtocolVersion, int> _headerSizes = ProtocolVersion.Range().ToFrozenDictionary(version => version, version =>
    {
        var mapping = CrossStitchService.ModArgumentMapping;
        var buffer = new BufferSpan(stackalloc byte[mapping.Identifier.Length * 2]);

        ArgumentSerializerRegistry.WriteParserIdentifier(ref buffer, mapping, version);
        return buffer.Position;
    });

    public static IArgumentSerializer Instance { get; } = new CrossStitchModArgumentSerializer();

    public IArgumentType Deserialize(ref BufferSpan buffer, ProtocolVersion protocolVersion)
    {
        ArgumentSerializerMapping identifier;
        int? parserId = null;

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_19)
        {
            var id = buffer.ReadVarInt();
            identifier = new ArgumentSerializerMapping("crossstitch:identified_" + (id < 0 ? "n" + (-id) : id), protocolVersion, id);
            parserId = id;
        }
        else
        {
            identifier = new ArgumentSerializerMapping(buffer.ReadString());
        }

        return new CrossStitchModArgumentType(identifier, new BufferMemory(buffer.ReadByteArray().ToArray()), parserId);
    }

    public void Serialize(IArgumentType argumentType, ref BufferSpan buffer, ProtocolVersion protocolVersion)
    {
        var modArgumentType = argumentType.As<CrossStitchModArgumentType>();
        var unwrappedMapping = modArgumentType.Mapping;

        // ArgumentSerializerRegistry already wrote the parser identifier, we need to go back and overwrite it with unwrapped one
        var headerSize = _headerSizes[protocolVersion];
        buffer.Seek(-headerSize);

        // The backend's parser ID is written back as read, not resolved through the proxy's parser table where -1 means removed.
        // An argument type the backend never registered arrives as -1, and the client skips it just as on a direct connection.
        if (modArgumentType.ParserId is { } parserId)
            buffer.WriteVarInt(parserId);
        else
            ArgumentSerializerRegistry.WriteParserIdentifier(ref buffer, unwrappedMapping, protocolVersion);

        var span = modArgumentType.Data.Span;
        buffer.Write(span.ReadToEnd());
    }
}
