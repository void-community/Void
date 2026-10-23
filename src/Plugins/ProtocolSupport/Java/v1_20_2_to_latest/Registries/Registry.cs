using Void.Minecraft.Network.Definitions;
using Void.Minecraft.Network.Registries.PacketId.Mappings;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Packets.Serverbound;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Packets.Clientbound;
using Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Packets.Serverbound;

namespace Void.Proxy.Plugins.ProtocolSupport.Java.v1_20_2_to_latest.Registries;

public static class Registry
{
    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ClientboundHandshakeMappings = new Dictionary<MinecraftPacketIdMapping[], Type>();

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ServerboundHandshakeMappings = new Dictionary<MinecraftPacketIdMapping[], Type> { { PacketIdDefinitions.ServerboundHandshake, typeof(HandshakePacket) } };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ClientboundStatusMappings = new Dictionary<MinecraftPacketIdMapping[], Type>();

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ServerboundStatusMappings = new Dictionary<MinecraftPacketIdMapping[], Type>();

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ClientboundLoginMappings = new Dictionary<MinecraftPacketIdMapping[], Type>
    {
        { PacketIdDefinitions.ClientboundLoginDisconnect, typeof(JsonDisconnectPacket) },
        { PacketIdDefinitions.ClientboundEncryptionRequest, typeof(EncryptionRequestPacket) },
        { PacketIdDefinitions.ClientboundLoginSuccess, typeof(LoginSuccessPacket) },
        { PacketIdDefinitions.ClientboundSetCompression, typeof(SetCompressionPacket) },
        { PacketIdDefinitions.ClientboundLoginPluginRequest, typeof(LoginPluginRequestPacket) }
    };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ServerboundLoginMappings = new Dictionary<MinecraftPacketIdMapping[], Type> { { PacketIdDefinitions.ServerboundLoginStart, typeof(LoginStartPacket) }, { PacketIdDefinitions.ServerboundEncryptionResponse, typeof(EncryptionResponsePacket) }, { PacketIdDefinitions.ServerboundLoginPluginResponse, typeof(LoginPluginResponsePacket) }, { PacketIdDefinitions.ServerboundLoginAcknowledged, typeof(LoginAcknowledgedPacket) } };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ClientboundConfigurationMappings = new Dictionary<MinecraftPacketIdMapping[], Type> { { PacketIdDefinitions.ClientboundConfigurationDisconnect, typeof(NbtDisconnectPacket) }, { PacketIdDefinitions.ClientboundConfigurationFinishConfiguration, typeof(FinishConfigurationPacket) }, { PacketIdDefinitions.ClientboundConfigurationKeepAliveRequest, typeof(KeepAliveRequestPacket) } };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ServerboundConfigurationMappings = new Dictionary<MinecraftPacketIdMapping[], Type> { { PacketIdDefinitions.ServerboundAcknowledgeFinishConfiguration, typeof(AcknowledgeFinishConfigurationPacket) }, { PacketIdDefinitions.ServerboundConfigurationKeepAliveResponse, typeof(KeepAliveResponsePacket) } };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ClientboundPlayMappings = new Dictionary<MinecraftPacketIdMapping[], Type>
    {
        { PacketIdDefinitions.ClientboundBundleDelimiter, typeof(BundleDelimiterPacket) },
        { PacketIdDefinitions.ClientboundPlayKeepAliveRequest, typeof(KeepAliveRequestPacket) },
        { PacketIdDefinitions.ClientboundStartConfiguration, typeof(StartConfigurationPacket) },
        { PacketIdDefinitions.ClientboundPlayDisconnect, typeof(NbtDisconnectPacket) },
        { PacketIdDefinitions.ClientboundSystemChatMessage, typeof(SystemChatMessagePacket) },
        { PacketIdDefinitions.ClientboundCommands, typeof(CommandsPacket) },
        { PacketIdDefinitions.ClientboundCommandSuggestionsResponse, typeof(CommandSuggestionsResponsePacket) }
    };

    public static readonly IReadOnlyDictionary<MinecraftPacketIdMapping[], Type> ServerboundPlayMappings = new Dictionary<MinecraftPacketIdMapping[], Type>
    {
        { PacketIdDefinitions.ServerboundChatCommand, typeof(ChatCommandPacket) },
        { PacketIdDefinitions.ServerboundSignedChatCommand, typeof(SignedChatCommandPacket) },
        { PacketIdDefinitions.ServerboundPlayKeepAliveResponse, typeof(KeepAliveResponsePacket) },
        { PacketIdDefinitions.ServerboundAcknowledgeConfiguration, typeof(AcknowledgeConfigurationPacket) },
        { PacketIdDefinitions.ServerboundCommandSuggestionsRequest, typeof(CommandSuggestionsRequestPacket) }
    };

    public static void Fill()
    {
        // will initialize static fields
    }
}
