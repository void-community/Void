using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.PacketId;
using Void.Minecraft.Network.Registries.PacketId.Mappings;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Messages;

namespace Void.Proxy.Plugins.Common.Network.Registries.PacketId;

public class MinecraftPacketIdRegistry : IMinecraftPacketIdRegistry
{
    private readonly Dictionary<int, Type> _mappings = [];
    private readonly Dictionary<Type, int> _reverseMappings = [];

    // Links read the mappings as plugins unload and phases change them, so every access takes the registry's lock
    public IEnumerable<Type> PacketTypes
    {
        get
        {
            lock (this)
                return [.. _mappings.Values];
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (this)
                return this is { _mappings.Count: 0, _reverseMappings.Count: 0 };
        }
    }

    public bool Contains<T>() where T : IMinecraftPacket
    {
        return Contains(typeof(T));
    }

    public bool Contains(INetworkMessage message)
    {
        return Contains(message.GetType());
    }

    public bool Contains(Type type)
    {
        lock (this)
            return _reverseMappings.Keys.Any(type.IsAssignableFrom);
    }

    public bool TryCreateDecoder(int id, [MaybeNullWhen(false)] out MinecraftPacketDecoder<IMinecraftPacket> decoder)
    {
        decoder = null;

        if (!TryGetType(id, out var type))
            return false;

        return TryCreateDecoder(type, out decoder);
    }

    public bool TryCreateDecoder(int id, [MaybeNullWhen(false)] out Type type, [MaybeNullWhen(false)] out MinecraftPacketDecoder<IMinecraftPacket> decoder)
    {
        decoder = null;

        if (!TryGetType(id, out type))
            return false;

        return TryCreateDecoder(type, out decoder);
    }

    public bool TryGetType(int id, [MaybeNullWhen(false)] out Type packetType)
    {
        lock (this)
            return _mappings.TryGetValue(id, out packetType);
    }

    public bool TryGetPacketId(IMinecraftPacket packet, [MaybeNullWhen(false)] out int id)
    {
        lock (this)
            return _reverseMappings.TryGetValue(packet.GetType(), out id);
    }

    public IMinecraftPacketIdRegistry ReplacePackets(IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> registry, ProtocolVersion protocolVersion)
    {
        lock (this)
        {
            Clear();
            AddPackets(registry, protocolVersion);
        }

        return this;
    }

    public IMinecraftPacketIdRegistry AddPackets(IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> registry, ProtocolVersion protocolVersion)
    {
        lock (this)
            AddPacketsCore(registry, protocolVersion);

        return this;
    }

    private void AddPacketsCore(IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> registry, ProtocolVersion protocolVersion)
    {
        foreach (var (mappings, type) in registry)
        {
            if (mappings.Length == 0)
                continue;

            for (var i = 0; i < mappings.Length; i++)
            {
                var current = mappings[i];
                var next = i + 1 < mappings.Length ? mappings[i + 1] : current;

                var from = current.ProtocolVersion;
                var to = current.LastValidProtocolVersion ?? (current.Equals(next) ? ProtocolVersion.Latest : next.ProtocolVersion - 1);

                if (from.CompareTo(to) > 0)
                    throw new ArgumentException($"Next mapping version ({to}) should be lower than the current ({from}) for {type}");

                if (!ProtocolVersion.Range(from, to).Contains(protocolVersion))
                    continue;

                if (!_mappings.TryAdd(current.Id, type) || !_reverseMappings.TryAdd(type, current.Id))
                    throw new ArgumentException($"{type} cannot be registered with packet Id {current.Id}, because there is already {_mappings[current.Id].FullName}");
            }
        }
    }

    public void Clear()
    {
        lock (this)
        {
            _mappings.Clear();
            _reverseMappings.Clear();
        }
    }

    public void Clear(Direction direction)
    {
        var directionType =
            direction is Direction.Clientbound ? typeof(IMinecraftClientboundPacket) :
            direction is Direction.Serverbound ? typeof(IMinecraftServerboundPacket) :
            throw new ArgumentOutOfRangeException(nameof(direction), direction, null);

        lock (this)
        {
            var filtered = _mappings.Where(mapping => mapping.Value.IsAssignableTo(directionType)).ToArray();

            foreach (var (id, type) in filtered)
            {
                _mappings.Remove(id);
                _reverseMappings.Remove(type);
            }
        }
    }

    private static bool TryCreateDecoder(Type type, [MaybeNullWhen(false)] out MinecraftPacketDecoder<IMinecraftPacket> packet)
    {
        packet = null;

        var decodeMethod = type.GetMethod(nameof(IMinecraftPacket<>.Decode), BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        if (decodeMethod is null)
            return false;

        packet = decodeMethod.CreateDelegate<MinecraftPacketDecoder<IMinecraftPacket>>();
        return true;
    }
}
