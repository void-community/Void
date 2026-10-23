using Void.Minecraft.Network.Registries.PacketId.Mappings;

namespace Void.Minecraft.Network.Definitions;

// Thanks to Velocity Contributors!
// https://github.com/PaperMC/Velocity/

/// <summary>
/// Provides built-in packet identifier mappings across supported Java Edition protocol versions.
/// </summary>
/// <remarks>The arrays are mutable shared definitions; consumers should treat their contents as read-only.</remarks>
public static class PacketIdDefinitions
{
    #region Handshake - Serverbound

    /// <summary>Mappings for the serverbound handshake packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundHandshake =
    [
        new(0x00, ProtocolVersion.Oldest)
    ];

    #endregion

    #region Example Plugin Definitions

    /// <summary>Mappings for the clientbound set-held-item packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundSetHeldItem =
    [
        new(0x09, ProtocolVersion.Oldest),
        new(0x38, ProtocolVersion.MINECRAFT_1_8),
        new(0x37, ProtocolVersion.MINECRAFT_1_9),
        new(0x3B, ProtocolVersion.MINECRAFT_1_12),
        new(0x3A, ProtocolVersion.MINECRAFT_1_13),
        new(0x3F, ProtocolVersion.MINECRAFT_1_17),
        new(0x3C, ProtocolVersion.MINECRAFT_1_18),
        new(0x48, ProtocolVersion.MINECRAFT_1_19),
        new(0x4A, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x4C, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x4D, ProtocolVersion.MINECRAFT_1_20),
        new(0x4F, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x51, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x53, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x63, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x62, ProtocolVersion.MINECRAFT_1_21_5),
        new(0x67, ProtocolVersion.MINECRAFT_1_21_9),
        new(0x69, ProtocolVersion.MINECRAFT_26_1),
        new(0x6B, ProtocolVersion.MINECRAFT_26_3)
    ];

    /// <summary>Mappings for the serverbound set-held-item packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundSetHeldItem =
    [
        new(0x09, ProtocolVersion.Oldest),
        new(0x16, ProtocolVersion.MINECRAFT_1_9),
        new(0x1A, ProtocolVersion.MINECRAFT_1_12),
        new(0x25, ProtocolVersion.MINECRAFT_1_13),
        new(0x28, ProtocolVersion.MINECRAFT_1_17),
        new(0x29, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x28, ProtocolVersion.MINECRAFT_1_20),
        new(0x2B, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x2C, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x2F, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x31, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x33, ProtocolVersion.MINECRAFT_1_21_4),
        new(0x34, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x35, ProtocolVersion.MINECRAFT_26_1),
        new(0x36, ProtocolVersion.MINECRAFT_26_3)
    ];

    #endregion

    #region Login - Serverbound

    /// <summary>Mappings for the serverbound login-start packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundLoginStart =
    [
        new(0x00, ProtocolVersion.Oldest)
    ];

    /// <summary>
    /// Maps the serverbound login encryption response packet to its packet ID for all supported protocol versions.
    /// </summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundEncryptionResponse =
    [
        new(0x01, ProtocolVersion.Oldest)
    ];

    /// <summary>Mappings for the serverbound login plugin-response packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundLoginPluginResponse =
    [
        new(0x02, ProtocolVersion.Oldest)
    ];

    /// <summary>Mappings for the serverbound login-acknowledged packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundLoginAcknowledged =
    [
        new(0x03, ProtocolVersion.Oldest)
    ];

    #endregion

    #region Login - Clientbound

    /// <summary>Mappings for the clientbound login disconnect packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundLoginDisconnect =
    [
        new(0x00, ProtocolVersion.Oldest)
    ];

    /// <summary>Mappings for the clientbound encryption-request packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundEncryptionRequest =
    [
        new(0x01, ProtocolVersion.Oldest)
    ];

    /// <summary>Mappings for the clientbound login-success packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundLoginSuccess =
    [
        new(0x02, ProtocolVersion.Oldest)
    ];

    /// <summary>Mappings for the clientbound set-compression packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundSetCompression =
    [
        new(0x03, ProtocolVersion.MINECRAFT_1_8)
    ];

    /// <summary>Mappings for the clientbound login plugin-request packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundLoginPluginRequest =
    [
        new(0x04, ProtocolVersion.Oldest)
    ];

    #endregion

    #region Configuration - Serverbound

    /// <summary>Mappings for the serverbound finish-configuration acknowledgement packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundAcknowledgeFinishConfiguration =
    [
        new(0x02, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x03, ProtocolVersion.MINECRAFT_1_20_5)
    ];

    /// <summary>Mappings for the serverbound configuration keep-alive response packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundConfigurationKeepAliveResponse =
    [
        new(0x03, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x04, ProtocolVersion.MINECRAFT_1_20_5)
    ];

    #endregion

    #region Configuration - Clientbound

    /// <summary>Mappings for the clientbound configuration disconnect packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundConfigurationDisconnect =
    [
        new(0x01, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x02, ProtocolVersion.MINECRAFT_1_20_5)
    ];

    /// <summary>Mappings for the clientbound finish-configuration packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundConfigurationFinishConfiguration =
    [
        new(0x02, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x03, ProtocolVersion.MINECRAFT_1_20_5)
    ];

    /// <summary>Mappings for the clientbound configuration keep-alive request packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundConfigurationKeepAliveRequest =
    [
        new(0x03, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x04, ProtocolVersion.MINECRAFT_1_20_5)
    ];

    #endregion

    #region Play - Serverbound

    /// <summary>Mappings for the serverbound play keep-alive response packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundPlayKeepAliveResponse =
    [
        new(0x00, ProtocolVersion.Oldest),
        new(0x0B, ProtocolVersion.MINECRAFT_1_9),
        new(0x0C, ProtocolVersion.MINECRAFT_1_12),
        new(0x0B, ProtocolVersion.MINECRAFT_1_12_1),
        new(0x0E, ProtocolVersion.MINECRAFT_1_13),
        new(0x0F, ProtocolVersion.MINECRAFT_1_14),
        new(0x10, ProtocolVersion.MINECRAFT_1_16),
        new(0x0F, ProtocolVersion.MINECRAFT_1_17),
        new(0x11, ProtocolVersion.MINECRAFT_1_19),
        new(0x12, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x11, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x12, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x14, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x15, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x18, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x1A, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x1B, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x1C, ProtocolVersion.MINECRAFT_26_1)
    ];

    /// <summary>Mappings for the legacy serverbound chat-message packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundChatMessage =
    [
        new(0x01, ProtocolVersion.Oldest),
        new(0x02, ProtocolVersion.MINECRAFT_1_9),
        new(0x03, ProtocolVersion.MINECRAFT_1_12),
        new(0x02, ProtocolVersion.MINECRAFT_1_12_1),
        new(0x02, ProtocolVersion.MINECRAFT_1_13),
        new(0x03, ProtocolVersion.MINECRAFT_1_14, ProtocolVersion.MINECRAFT_1_18_2)
    ];

    /// <summary>Mappings for the serverbound keyed chat-command packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundKeyedChatCommand =
    [
        new(0x03, ProtocolVersion.MINECRAFT_1_19),
        new(0x04, ProtocolVersion.MINECRAFT_1_19_1, ProtocolVersion.MINECRAFT_1_19_1)
    ];

    /// <summary>Mappings for the serverbound signed chat-command packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundSignedChatCommand =
    [
        new(0x04, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x04, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x05, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x06, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x07, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x08, ProtocolVersion.MINECRAFT_26_1)
    ];

    /// <summary>Mappings for the serverbound unsigned chat-command packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundChatCommand =
    [
        new(0x04, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x05, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x06, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x07, ProtocolVersion.MINECRAFT_26_1)
    ];

    /// <summary>Mappings for the serverbound acknowledge-configuration packet sent from play.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundAcknowledgeConfiguration =
    [
        new(0x0B, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x0C, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x0E, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x0F, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x10, ProtocolVersion.MINECRAFT_26_1)
    ];

    /// <summary>Mappings for the serverbound play plugin-message packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundPlayPluginMessage =
    [
        new(0x17, ProtocolVersion.Oldest),
        new(0x09, ProtocolVersion.MINECRAFT_1_9),
        new(0x0A, ProtocolVersion.MINECRAFT_1_12),
        new(0x09, ProtocolVersion.MINECRAFT_1_12_1),
        new(0x0A, ProtocolVersion.MINECRAFT_1_13),
        new(0x0B, ProtocolVersion.MINECRAFT_1_14),
        new(0x0A, ProtocolVersion.MINECRAFT_1_17),
        new(0x0C, ProtocolVersion.MINECRAFT_1_19),
        new(0x0D, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x0C, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x0D, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x0F, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x10, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x12, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x14, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x15, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x16, ProtocolVersion.MINECRAFT_26_1)
    ];

    /// <summary>Mappings for the serverbound command-suggestions request packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ServerboundCommandSuggestionsRequest =
    [
        new(0x14, ProtocolVersion.MINECRAFT_1_7_2),
        new(0x01, ProtocolVersion.MINECRAFT_1_9),
        new(0x02, ProtocolVersion.MINECRAFT_1_12),
        new(0x01, ProtocolVersion.MINECRAFT_1_12_1),
        new(0x05, ProtocolVersion.MINECRAFT_1_13),
        new(0x06, ProtocolVersion.MINECRAFT_1_14),
        new(0x08, ProtocolVersion.MINECRAFT_1_19),
        new(0x09, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x08, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x09, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x0A, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x0B, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x0D, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x0E, ProtocolVersion.MINECRAFT_1_21_6),
        new(0x0F, ProtocolVersion.MINECRAFT_26_1)
    ];

    #endregion

    #region Play - Clientbound

    /// <summary>Mappings for the clientbound play keep-alive request packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundPlayKeepAliveRequest =
    [
        new(0x00, ProtocolVersion.Oldest),
        new(0x1F, ProtocolVersion.MINECRAFT_1_9),
        new(0x21, ProtocolVersion.MINECRAFT_1_13),
        new(0x20, ProtocolVersion.MINECRAFT_1_14),
        new(0x21, ProtocolVersion.MINECRAFT_1_15),
        new(0x20, ProtocolVersion.MINECRAFT_1_16),
        new(0x1F, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x21, ProtocolVersion.MINECRAFT_1_17),
        new(0x1E, ProtocolVersion.MINECRAFT_1_19),
        new(0x20, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x1F, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x23, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x24, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x26, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x27, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x26, ProtocolVersion.MINECRAFT_1_21_5),
        new(0x2B, ProtocolVersion.MINECRAFT_1_21_9),
        new(0x2C, ProtocolVersion.MINECRAFT_26_1),
        new(0x2D, ProtocolVersion.MINECRAFT_26_3)
    ];

    /// <summary>Mappings for the clientbound play disconnect packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundPlayDisconnect =
    [
        new(0x40, ProtocolVersion.Oldest),
        new(0x1A, ProtocolVersion.MINECRAFT_1_9),
        new(0x1B, ProtocolVersion.MINECRAFT_1_13),
        new(0x1A, ProtocolVersion.MINECRAFT_1_14),
        new(0x1B, ProtocolVersion.MINECRAFT_1_15),
        new(0x1A, ProtocolVersion.MINECRAFT_1_16),
        new(0x19, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x1A, ProtocolVersion.MINECRAFT_1_17),
        new(0x17, ProtocolVersion.MINECRAFT_1_19),
        new(0x19, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x17, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x1A, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x1B, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x1D, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x1C, ProtocolVersion.MINECRAFT_1_21_5),
        new(0x20, ProtocolVersion.MINECRAFT_1_21_9)
    ];

    /// <summary>Mappings for the clientbound join-game packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundJoinGame =
    [
        new(0x01, ProtocolVersion.Oldest),
        new(0x23, ProtocolVersion.MINECRAFT_1_9),
        new(0x25, ProtocolVersion.MINECRAFT_1_13),
        new(0x25, ProtocolVersion.MINECRAFT_1_14),
        new(0x26, ProtocolVersion.MINECRAFT_1_15),
        new(0x25, ProtocolVersion.MINECRAFT_1_16),
        new(0x24, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x26, ProtocolVersion.MINECRAFT_1_17),
        new(0x23, ProtocolVersion.MINECRAFT_1_19),
        new(0x25, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x24, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x28, ProtocolVersion.MINECRAFT_1_19_4)
    ];

    /// <summary>Mappings for the clientbound respawn packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundRespawn =
    [
        new(0x07, ProtocolVersion.Oldest),
        new(0x33, ProtocolVersion.MINECRAFT_1_9),
        new(0x34, ProtocolVersion.MINECRAFT_1_12),
        new(0x35, ProtocolVersion.MINECRAFT_1_12_1),
        new(0x38, ProtocolVersion.MINECRAFT_1_13),
        new(0x3A, ProtocolVersion.MINECRAFT_1_14),
        new(0x3B, ProtocolVersion.MINECRAFT_1_15),
        new(0x3A, ProtocolVersion.MINECRAFT_1_16),
        new(0x39, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x3D, ProtocolVersion.MINECRAFT_1_17),
        new(0x3B, ProtocolVersion.MINECRAFT_1_19),
        new(0x3E, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x3D, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x41, ProtocolVersion.MINECRAFT_1_19_4)
    ];

    /// <summary>Mappings for the legacy clientbound chat-message packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundChatMessage =
    [
        new(0x02, ProtocolVersion.Oldest),
        new(0x0F, ProtocolVersion.MINECRAFT_1_9),
        new(0x0E, ProtocolVersion.MINECRAFT_1_13),
        new(0x0F, ProtocolVersion.MINECRAFT_1_15),
        new(0x0E, ProtocolVersion.MINECRAFT_1_16),
        new(0x0F, ProtocolVersion.MINECRAFT_1_17, ProtocolVersion.MINECRAFT_1_18_2)
    ];

    /// <summary>Mappings for the clientbound bundle-delimiter packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundBundleDelimiter =
    [
        new(0x00, ProtocolVersion.MINECRAFT_1_19_4)
    ];

    /// <summary>Mappings for the clientbound system-chat packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundSystemChatMessage =
    [
        new(0x5F, ProtocolVersion.MINECRAFT_1_19),
        new(0x62, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x60, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x64, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x67, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x69, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x6C, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x73, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x72, ProtocolVersion.MINECRAFT_1_21_5),
        new(0x77, ProtocolVersion.MINECRAFT_1_21_9),
        new(0x79, ProtocolVersion.MINECRAFT_26_1),
        new(0x7C, ProtocolVersion.MINECRAFT_26_3)
    ];

    /// <summary>Mappings for the clientbound start-configuration packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundStartConfiguration =
    [
        new(0x65, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x67, ProtocolVersion.MINECRAFT_1_20_3),
        new(0x69, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x70, ProtocolVersion.MINECRAFT_1_21_2),
        new(0x6F, ProtocolVersion.MINECRAFT_1_21_5),
        new(0x74, ProtocolVersion.MINECRAFT_1_21_9),
        new(0x76, ProtocolVersion.MINECRAFT_26_1),
        new(0x78, ProtocolVersion.MINECRAFT_26_3)
    ];

    /// <summary>Mappings for the clientbound play plugin-message packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundPlayPluginMessage =
    [
        new(0x3F, ProtocolVersion.Oldest),
        new(0x18, ProtocolVersion.MINECRAFT_1_9),
        new(0x19, ProtocolVersion.MINECRAFT_1_13),
        new(0x18, ProtocolVersion.MINECRAFT_1_14),
        new(0x19, ProtocolVersion.MINECRAFT_1_15),
        new(0x18, ProtocolVersion.MINECRAFT_1_16),
        new(0x17, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x18, ProtocolVersion.MINECRAFT_1_17),
        new(0x15, ProtocolVersion.MINECRAFT_1_19),
        new(0x16, ProtocolVersion.MINECRAFT_1_19_1),
        new(0x15, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x17, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x18, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x19, ProtocolVersion.MINECRAFT_1_20_5),
        new(0x18, ProtocolVersion.MINECRAFT_1_21_5)
    ];

    /// <summary>Mappings for the clientbound commands packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundCommands =
    [
        new(0x11, ProtocolVersion.MINECRAFT_1_13),
        new(0x12, ProtocolVersion.MINECRAFT_1_15),
        new(0x11, ProtocolVersion.MINECRAFT_1_16),
        new(0x10, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x12, ProtocolVersion.MINECRAFT_1_17),
        new(0x0F, ProtocolVersion.MINECRAFT_1_19),
        new(0x0E, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x10, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x11, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x10, ProtocolVersion.MINECRAFT_1_21_5)
    ];

    /// <summary>Mappings for the clientbound command-suggestions response packet.</summary>
    public static readonly MinecraftPacketIdMapping[] ClientboundCommandSuggestionsResponse =
    [
        new(0x3A, ProtocolVersion.MINECRAFT_1_7_2),
        new(0x0E, ProtocolVersion.MINECRAFT_1_9),
        new(0x10, ProtocolVersion.MINECRAFT_1_13),
        new(0x11, ProtocolVersion.MINECRAFT_1_15),
        new(0x10, ProtocolVersion.MINECRAFT_1_16),
        new(0x0F, ProtocolVersion.MINECRAFT_1_16_2),
        new(0x11, ProtocolVersion.MINECRAFT_1_17),
        new(0x0E, ProtocolVersion.MINECRAFT_1_19),
        new(0x0D, ProtocolVersion.MINECRAFT_1_19_3),
        new(0x0F, ProtocolVersion.MINECRAFT_1_19_4),
        new(0x10, ProtocolVersion.MINECRAFT_1_20_2),
        new(0x0F, ProtocolVersion.MINECRAFT_1_21_5)
    ];

    #endregion
}
