using Void.Minecraft.Network;
using Void.Minecraft.Network.Messages;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.Transformations;
using Void.Minecraft.Network.Registries.Transformations.Mappings;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Plugins;

namespace Void.Proxy.Plugins.Common.Network.Registries.Transformations;

public class MinecraftPacketTransformationsPluginsRegistry : IMinecraftPacketTransformationsPluginsRegistry
{
    private Dictionary<IPlugin, IMinecraftPacketTransformationsRegistry> _map = [];

    public bool IsEmpty => All.All(registry => registry.IsEmpty);
    public ProtocolVersion? ProtocolVersion { get; set; }
    public IPlugin? ManagedBy { get; set; }
    // Links read the registries as plugins unload and phases change them, so each read takes a snapshot
    public IReadOnlyCollection<IMinecraftPacketTransformationsRegistry> All
    {
        get
        {
            lock (this)
                return [.. _map.Values];
        }
    }

    public IMinecraftPacketTransformationsRegistry Get(IPlugin plugin)
    {
        lock (this)
        {
            if (ProtocolVersion is null)
                throw new InvalidOperationException("Protocol version is not set yet");

            if (!_map.TryGetValue(plugin, out var registry))
                _map[plugin] = registry = new MinecraftPacketTransformationsRegistry();

            return registry;
        }
    }

    public void Remove(IPlugin plugin)
    {
        lock (this)
            _map.Remove(plugin);
    }

    public bool Contains<T>(TransformationType type) where T : IMinecraftPacket
    {
        return All.Any(registry => registry.Contains<T>(type));
    }

    public bool Contains(IMinecraftMessage message, TransformationType type)
    {
        return All.Any(registry => registry.Contains(message, type));
    }

    public bool Contains(Type packetType, TransformationType transformationType)
    {
        return All.Any(registry => registry.Contains(packetType, transformationType));
    }

    public void Clear()
    {
        lock (this)
            _map = [];
    }

    public void Clear(Direction direction, Operation operation)
    {
        // TODO: Transformations registry is not operation-based?
        // Decide if it should be so.

        lock (this)
        {
            foreach (var (_, registry) in _map)
                registry.Clear(direction);
        }
    }

    public void Reset()
    {
        Clear();
        ProtocolVersion = null;
        ManagedBy = null;
    }
}
