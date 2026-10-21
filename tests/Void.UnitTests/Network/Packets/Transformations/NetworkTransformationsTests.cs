using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Void.Minecraft.Buffers;
using Void.Minecraft.Network;
using Void.Minecraft.Network.Definitions;
using Void.Minecraft.Network.Messages.Packets;
using Void.Minecraft.Network.Registries.PacketId.Mappings;
using Void.Minecraft.Network.Registries.Transformations.Extensions;
using Void.Minecraft.Network.Registries.Transformations.Mappings;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Channels;
using Void.Proxy.Api.Network.Streams.Recyclable;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Plugins.Common.Network.Channels;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.Common.Network.Packets.Transformations;
using Void.Proxy.Plugins.Common.Network.Streams.Packet;
using Void.Proxy.Plugins.Common.Services.Registries;
using Xunit;

namespace Void.UnitTests.Network.Packets.Transformations;

public class NetworkTransformationsTests
{
    [Fact]
    public async Task DisposeAsync_ClearsRegistryPluginReferencesAsync()
    {
        var packetStream = new MinecraftPacketMessageStream();
        var plugin = new TestPlugin();

        packetStream.Registries.Setup(plugin, ProtocolVersion.MINECRAFT_1_21_4);

        await packetStream.DisposeAsync();

        Assert.Null(packetStream.Registries.PacketIdSystem.ManagedBy);
        Assert.Null(packetStream.Registries.PacketIdPlugins.ManagedBy);
        Assert.Null(packetStream.Registries.PacketTransformationsSystem.ManagedBy);
        Assert.Null(packetStream.Registries.PacketTransformationsPlugins.ManagedBy);
    }

    [Fact]
    public void RegisterMappings_AfterServerRedirect_PreservesLegacyKeepAliveDecoding()
    {
        const int keepAliveId = 1687097462;

        var protocolVersion = ProtocolVersion.MINECRAFT_1_8;
        var packetStream = new MinecraftPacketMessageStream();
        using var channel = new SimpleMinecraftChannel(packetStream);
        var mappings = NetworkTransformations.KeepAlive.SelectMany(transformation => transformation.Mappings);

        packetStream.Registries.PacketIdSystem.ProtocolVersion = protocolVersion;
        packetStream.Registries.PacketIdSystem.ReplacePackets(Operation.Read, new Dictionary<MinecraftPacketIdMapping[], Type>
        {
            { PacketIdDefinitions.ServerboundPlayKeepAliveResponse, typeof(KeepAliveResponsePacket) }
        });

        RegisterMappings<KeepAliveResponsePacket>(channel, protocolVersion, mappings);
        Assert.True(packetStream.Registries.PacketTransformationsSystem.All.TryGetFor(typeof(KeepAliveResponsePacket), TransformationType.Upgrade, out var originalTransformations));

        RegisterMappings<KeepAliveResponsePacket>(channel, protocolVersion, mappings);
        Assert.True(packetStream.Registries.PacketTransformationsSystem.All.TryGetFor(typeof(KeepAliveResponsePacket), TransformationType.Upgrade, out var retainedTransformations));
        Assert.Same(originalTransformations, retainedTransformations);

        var stream = RecyclableStream.RecyclableMemoryStreamManager.GetStream();
        var buffer = new MinecraftBuffer(stream);
        buffer.WriteVarInt(PacketIdDefinitions.ServerboundPlayKeepAliveResponse[0].Id);
        buffer.WriteVarInt(keepAliveId);

        var packet = Assert.IsType<KeepAliveResponsePacket>(packetStream.DecodePacket(stream));

        Assert.Equal(keepAliveId, packet.Id);
    }

    [Fact]
    public void ClearLinkTransformations_WithoutReplacement_ClearsBothChannelsAndAllowsRegistrationAgain()
    {
        var protocolVersion = ProtocolVersion.MINECRAFT_1_8;
        var plugin = new TestPlugin();
        var mappings = NetworkTransformations.KeepAlive.SelectMany(transformation => transformation.Mappings);
        var playerPacketStream = new MinecraftPacketMessageStream();
        var serverPacketStream = new MinecraftPacketMessageStream();
        using var playerChannel = new SimpleMinecraftChannel(playerPacketStream);
        using var serverChannel = new SimpleMinecraftChannel(serverPacketStream);

        RegisterTransformations(playerPacketStream, playerChannel, plugin, protocolVersion, mappings);
        RegisterTransformations(serverPacketStream, serverChannel, plugin, protocolVersion, mappings);

        ClearLinkTransformations(playerChannel, serverChannel, preservePlayerChannel: false);

        Assert.True(playerPacketStream.Registries.PacketTransformationsSystem.IsEmpty);
        Assert.True(playerPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);
        Assert.True(serverPacketStream.Registries.PacketTransformationsSystem.IsEmpty);
        Assert.True(serverPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);

        RegisterTransformations(playerPacketStream, playerChannel, plugin, protocolVersion, mappings);
        RegisterTransformations(serverPacketStream, serverChannel, plugin, protocolVersion, mappings);

        Assert.False(playerPacketStream.Registries.PacketTransformationsSystem.IsEmpty);
        Assert.False(playerPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);
        Assert.False(serverPacketStream.Registries.PacketTransformationsSystem.IsEmpty);
        Assert.False(serverPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);
    }

    [Fact]
    public void ClearLinkTransformations_WithReplacement_PreservesPlayerChannelAndClearsServerChannel()
    {
        const int keepAliveId = 1687097462;

        var protocolVersion = ProtocolVersion.MINECRAFT_1_8;
        var plugin = new TestPlugin();
        var mappings = NetworkTransformations.KeepAlive.SelectMany(transformation => transformation.Mappings);
        var playerPacketStream = new MinecraftPacketMessageStream();
        var serverPacketStream = new MinecraftPacketMessageStream();
        using var playerChannel = new SimpleMinecraftChannel(playerPacketStream);
        using var serverChannel = new SimpleMinecraftChannel(serverPacketStream);

        RegisterTransformations(playerPacketStream, playerChannel, plugin, protocolVersion, mappings);
        RegisterTransformations(serverPacketStream, serverChannel, plugin, protocolVersion, mappings);
        SetupKeepAliveDecoder(playerPacketStream, protocolVersion);

        Assert.True(playerPacketStream.Registries.PacketTransformationsSystem.All.TryGetFor(typeof(KeepAliveResponsePacket), TransformationType.Upgrade, out var originalTransformations));

        ClearLinkTransformations(playerChannel, serverChannel, preservePlayerChannel: true);

        Assert.True(playerPacketStream.Registries.PacketTransformationsSystem.All.TryGetFor(typeof(KeepAliveResponsePacket), TransformationType.Upgrade, out var retainedTransformations));
        Assert.Same(originalTransformations, retainedTransformations);
        Assert.False(playerPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);
        Assert.True(serverPacketStream.Registries.PacketTransformationsSystem.IsEmpty);
        Assert.True(serverPacketStream.Registries.PacketTransformationsPlugins.IsEmpty);

        using var stream = RecyclableStream.RecyclableMemoryStreamManager.GetStream();
        var buffer = new MinecraftBuffer(stream);
        buffer.WriteVarInt(PacketIdDefinitions.ServerboundPlayKeepAliveResponse[0].Id);
        buffer.WriteVarInt(keepAliveId);

        var packet = Assert.IsType<KeepAliveResponsePacket>(playerPacketStream.DecodePacket(stream));

        Assert.Equal(keepAliveId, packet.Id);
    }

    [Theory]
    // Servers before 1.12 write a component with nothing but text as a JSON string
    [InlineData(5, "\"Welcome to the server!\"")]
    [InlineData(5, "{\"text\":\"Welcome to the server!\"}")]
    [InlineData(316, "\"Welcome to the server!\"")]
    public void DecodePacket_LegacyChatMessage_ReadsTextComponentsWrittenAsJsonStrings(int protocol, string json)
    {
        var protocolVersion = ProtocolVersion.Range().First(version => version.Value == protocol);
        var packetStream = new MinecraftPacketMessageStream();
        using var channel = new SimpleMinecraftChannel(packetStream);

        packetStream.Registries.PacketIdSystem.ProtocolVersion = protocolVersion;
        packetStream.Registries.PacketIdSystem.ReplacePackets(Operation.Read, new Dictionary<MinecraftPacketIdMapping[], Type>
        {
            { PacketIdDefinitions.ClientboundChatMessage, typeof(ChatMessagePacket) }
        });

        RegisterMappings<ChatMessagePacket>(channel, protocolVersion, NetworkTransformations.ChatMessage.SelectMany(transformation => transformation.Mappings));

        using var stream = RecyclableStream.RecyclableMemoryStreamManager.GetStream();
        var buffer = new MinecraftBuffer(stream);
        buffer.WriteVarInt(PacketIdDefinitions.ClientboundChatMessage.Last(mapping => mapping.ProtocolVersion <= protocolVersion).Id);
        buffer.WriteString(json);

        if (protocolVersion >= ProtocolVersion.MINECRAFT_1_8)
            buffer.WriteUnsignedByte(0);

        var packet = Assert.IsType<ChatMessagePacket>(packetStream.DecodePacket(stream));

        Assert.Equal("Welcome to the server!", packet.Message.AsText);
    }

    private static void RegisterTransformations(MinecraftPacketMessageStream packetStream, SimpleMinecraftChannel channel, IPlugin plugin, ProtocolVersion protocolVersion, IEnumerable<MinecraftPacketTransformationMapping> mappings)
    {
        packetStream.Registries.PacketTransformationsPlugins.ProtocolVersion = protocolVersion;

        RegisterMappings<KeepAliveResponsePacket>(channel, protocolVersion, mappings);
        packetStream.Registries.PacketTransformationsPlugins.Get(plugin).RegisterTransformations<KeepAliveResponsePacket>(protocolVersion, mappings);
    }

    private static void RegisterMappings<T>(INetworkChannel channel, ProtocolVersion protocolVersion, params IEnumerable<MinecraftPacketTransformationMapping> mappings) where T : IMinecraftPacket
    {
        const string methodName = "RegisterMappings";
        var method = typeof(NetworkTransformations).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).SingleOrDefault(method => method.Name == methodName && method.GetParameters().Length is 3) ?? throw new MissingMethodException(typeof(NetworkTransformations).FullName, methodName);
        method.MakeGenericMethod(typeof(T)).Invoke(null, [channel, protocolVersion, mappings]);
    }

    private static void ClearLinkTransformations(INetworkChannel playerChannel, INetworkChannel serverChannel, bool preservePlayerChannel)
    {
        const string methodName = "ClearLinkTransformations";
        var method = typeof(AbstractRegistryService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(typeof(AbstractRegistryService).FullName, methodName);
        method.Invoke(null, [playerChannel, serverChannel, preservePlayerChannel]);
    }

    private static void SetupKeepAliveDecoder(MinecraftPacketMessageStream packetStream, ProtocolVersion protocolVersion)
    {
        packetStream.Registries.PacketIdSystem.ProtocolVersion = protocolVersion;
        packetStream.Registries.PacketIdSystem.ReplacePackets(Operation.Read, new Dictionary<MinecraftPacketIdMapping[], Type>
        {
            { PacketIdDefinitions.ServerboundPlayKeepAliveResponse, typeof(KeepAliveResponsePacket) }
        });
    }

    private sealed class TestPlugin : IPlugin
    {
        public string Name => nameof(TestPlugin);
    }
}
