using System;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes;
using Void.Minecraft.Commands.Brigadier.ArgumentTypes.RegistryKey;
using Void.Minecraft.Commands.Brigadier.Registry;
using Void.Minecraft.Commands.Brigadier.Serializers;
using Void.Minecraft.Commands.Brigadier.Serializers.Passthrough;

namespace Void.Minecraft.Network.Definitions;

// Thanks to Velocity Contributors!
// https://github.com/PaperMC/Velocity/

/// <summary>
/// Provides built-in Brigadier and Minecraft command argument parser definitions.
/// </summary>
/// <remarks>The arrays and their mapping objects are mutable shared definitions; consumers should treat them as read-only.</remarks>
public class ArgumentParserDefinitions
{
    #region Brigadier

    /// <summary>
    /// Definitions for the primitive parsers in the <c>brigadier</c> namespace.
    /// </summary>
    public static readonly ArgumentParserDefinition[] BrigadierArgumentParserDefinitions =
    [
        ArgumentParserDefinition.From<BoolArgumentType>(BoolArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:bool", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 0
            })),
        ArgumentParserDefinition.From<FloatArgumentType>(FloatArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:float", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 1
            })),
        ArgumentParserDefinition.From<DoubleArgumentType>(DoubleArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:double", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 2
            })),
        ArgumentParserDefinition.From<IntegerArgumentType>(IntegerArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:integer", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 3
            })),
        ArgumentParserDefinition.From<LongArgumentType>(LongArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:long", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 4
            })),
        ArgumentParserDefinition.From<StringArgumentType>(StringArgumentSerializer.Instance,
            new ArgumentSerializerMapping("brigadier:string", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 5
            })),
    ];

    #endregion
    #region Minecraft

    /// <summary>
    /// Definitions for protocol-specific parsers in the <c>minecraft</c> namespace.
    /// </summary>
    public static readonly ArgumentParserDefinition[] MinecraftArgumentParserDefinitions =
    [
        #region Non-typed with non-empty serializers

        ArgumentParserDefinition.From(ByteArgumentPassthroughSerializer.Instance,
            new ArgumentSerializerMapping("minecraft:entity", new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 6
            })),
        ArgumentParserDefinition.From(ByteArgumentPassthroughSerializer.Instance,
            new ArgumentSerializerMapping("minecraft:score_holder",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 31,
                [ProtocolVersion.MINECRAFT_1_20_3] = 30,
                [ProtocolVersion.MINECRAFT_1_19] = 29
            })),
        ArgumentParserDefinition.From(TimeArgumentPassthroughSerializer.Instance,
            new ArgumentSerializerMapping("minecraft:time",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 43,
                [ProtocolVersion.MINECRAFT_1_20_5] = 42,
                [ProtocolVersion.MINECRAFT_1_20_3] = 41,
                [ProtocolVersion.MINECRAFT_1_19_3] = 40,
                [ProtocolVersion.MINECRAFT_1_19] = 42
            })),

        #endregion
        #region Typed with non-empty serializers

        ArgumentParserDefinition.From<RegistryKeyArgumentType>(RegistryKeyArgumentSerializer.Instance,
            new ArgumentSerializerMapping("minecraft:resource_or_tag",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 44,
                [ProtocolVersion.MINECRAFT_1_20_5] = 43,
                [ProtocolVersion.MINECRAFT_1_20_3] = 42,
                [ProtocolVersion.MINECRAFT_1_19_3] = 41,
                [ProtocolVersion.MINECRAFT_1_19] = 43
            })),

        ArgumentParserDefinition.From<ResourceOrTagKeyArgumentType>(ResourceOrTagKeyArgumentType.Serializer.Instance,
            new ArgumentSerializerMapping("minecraft:resource_or_tag_key",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 45,
                [ProtocolVersion.MINECRAFT_1_20_5] = 44,
                [ProtocolVersion.MINECRAFT_1_20_3] = 43,
                [ProtocolVersion.MINECRAFT_1_19_3] = 42
            })),

        ArgumentParserDefinition.From<RegistryKeyArgumentType>(RegistryKeyArgumentSerializer.Instance,
            new ArgumentSerializerMapping("minecraft:resource",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 46,
                [ProtocolVersion.MINECRAFT_1_20_5] = 45,
                [ProtocolVersion.MINECRAFT_1_20_3] = 44,
                [ProtocolVersion.MINECRAFT_1_19_3] = 43,
                [ProtocolVersion.MINECRAFT_1_19] = 44
            })),

        ArgumentParserDefinition.From<ResourceKeyArgumentType>(ResourceKeyArgumentType.Serializer.Instance,
            new ArgumentSerializerMapping("minecraft:resource_key",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 47,
                [ProtocolVersion.MINECRAFT_1_20_5] = 46,
                [ProtocolVersion.MINECRAFT_1_20_3] = 45,
                [ProtocolVersion.MINECRAFT_1_19_3] = 44
            })),
        ArgumentParserDefinition.From<ResourceSelectorArgumentType>(ResourceSelectorArgumentType.Serializer.Instance,
            new ArgumentSerializerMapping("minecraft:resource_selector",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 48,
                [ProtocolVersion.MINECRAFT_1_21_5] = 47
            })),

        #endregion
        #region Non-typed with empty serializers

        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:nbt")),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:game_profile",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 7
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:block_pos",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 8
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:column_pos",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 9
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:vec3",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 10
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:vec2",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 11
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:block_state",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 12
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:block_predicate",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 13
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:item_stack",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 14
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:item_predicate",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19] = 15
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:color",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_2] = -1,
                [ProtocolVersion.MINECRAFT_1_19] = 16
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:team_color",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_2] = 16
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:component",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 18,
                [ProtocolVersion.MINECRAFT_1_19] = 17
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:style",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 19,
                [ProtocolVersion.MINECRAFT_1_20_3] = 18
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:message",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 20,
                [ProtocolVersion.MINECRAFT_1_20_3] = 19,
                [ProtocolVersion.MINECRAFT_1_19] = 18
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:nbt_compound_tag",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 21,
                [ProtocolVersion.MINECRAFT_1_20_3] = 20,
                [ProtocolVersion.MINECRAFT_1_19] = 19
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:nbt_tag",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 22,
                [ProtocolVersion.MINECRAFT_1_20_3] = 21,
                [ProtocolVersion.MINECRAFT_1_19] = 20
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:nbt_path",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 23,
                [ProtocolVersion.MINECRAFT_1_20_3] = 22,
                [ProtocolVersion.MINECRAFT_1_19] = 21
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:objective",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 24,
                [ProtocolVersion.MINECRAFT_1_20_3] = 23,
                [ProtocolVersion.MINECRAFT_1_19] = 22
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:objective_criteria",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 25,
                [ProtocolVersion.MINECRAFT_1_20_3] = 24,
                [ProtocolVersion.MINECRAFT_1_19] = 23
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:operation",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 26,
                [ProtocolVersion.MINECRAFT_1_20_3] = 25,
                [ProtocolVersion.MINECRAFT_1_19] = 24
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:particle",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 27,
                [ProtocolVersion.MINECRAFT_1_20_3] = 26,
                [ProtocolVersion.MINECRAFT_1_19] = 25
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:angle",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 28,
                [ProtocolVersion.MINECRAFT_1_20_3] = 27,
                [ProtocolVersion.MINECRAFT_1_19] = 26
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:rotation",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 29,
                [ProtocolVersion.MINECRAFT_1_20_3] = 28,
                [ProtocolVersion.MINECRAFT_1_19] = 27
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:scoreboard_slot",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 30,
                [ProtocolVersion.MINECRAFT_1_20_3] = 29,
                [ProtocolVersion.MINECRAFT_1_19] = 28
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:swizzle",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 32,
                [ProtocolVersion.MINECRAFT_1_20_3] = 31,
                [ProtocolVersion.MINECRAFT_1_19] = 30
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:team",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 33,
                [ProtocolVersion.MINECRAFT_1_20_3] = 32,
                [ProtocolVersion.MINECRAFT_1_19] = 31
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:item_slot",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 34,
                [ProtocolVersion.MINECRAFT_1_20_3] = 33,
                [ProtocolVersion.MINECRAFT_1_19] = 32
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:item_slots",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 35,
                [ProtocolVersion.MINECRAFT_1_20_5] = 34
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:resource_location",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 36,
                [ProtocolVersion.MINECRAFT_1_20_5] = 35,
                [ProtocolVersion.MINECRAFT_1_20_3] = 34,
                [ProtocolVersion.MINECRAFT_1_19] = 33
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:mob_effect",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19_3] = -1,
                [ProtocolVersion.MINECRAFT_1_19] = 34
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:function",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 37,
                [ProtocolVersion.MINECRAFT_1_20_5] = 36,
                [ProtocolVersion.MINECRAFT_1_20_3] = 35,
                [ProtocolVersion.MINECRAFT_1_19_3] = 34,
                [ProtocolVersion.MINECRAFT_1_19] = 35
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:entity_anchor",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 38,
                [ProtocolVersion.MINECRAFT_1_20_5] = 37,
                [ProtocolVersion.MINECRAFT_1_20_3] = 36,
                [ProtocolVersion.MINECRAFT_1_19_3] = 35,
                [ProtocolVersion.MINECRAFT_1_19] = 36
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:int_range",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 39,
                [ProtocolVersion.MINECRAFT_1_20_5] = 38,
                [ProtocolVersion.MINECRAFT_1_20_3] = 37,
                [ProtocolVersion.MINECRAFT_1_19_3] = 36,
                [ProtocolVersion.MINECRAFT_1_19] = 37
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:float_range",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 40,
                [ProtocolVersion.MINECRAFT_1_20_5] = 39,
                [ProtocolVersion.MINECRAFT_1_20_3] = 38,
                [ProtocolVersion.MINECRAFT_1_19_3] = 37,
                [ProtocolVersion.MINECRAFT_1_19] = 38
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:item_enchantment",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19_3] = -1,
                [ProtocolVersion.MINECRAFT_1_19] = 39
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:entity_summon",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_19_3] = -1,
                [ProtocolVersion.MINECRAFT_1_19] = 40
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:dimension",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 41,
                [ProtocolVersion.MINECRAFT_1_20_5] = 40,
                [ProtocolVersion.MINECRAFT_1_20_3] = 39,
                [ProtocolVersion.MINECRAFT_1_19_3] = 38,
                [ProtocolVersion.MINECRAFT_1_19] = 41
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:gamemode",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 42,
                [ProtocolVersion.MINECRAFT_1_20_5] = 41,
                [ProtocolVersion.MINECRAFT_1_20_3] = 40,
                [ProtocolVersion.MINECRAFT_1_19_3] = 39
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:template_mirror",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 49,
                [ProtocolVersion.MINECRAFT_1_21_5] = 48,
                [ProtocolVersion.MINECRAFT_1_20_5] = 47,
                [ProtocolVersion.MINECRAFT_1_20_3] = 46,
                [ProtocolVersion.MINECRAFT_1_19] = 45
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:template_rotation",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 50,
                [ProtocolVersion.MINECRAFT_1_21_5] = 49,
                [ProtocolVersion.MINECRAFT_1_20_5] = 48,
                [ProtocolVersion.MINECRAFT_1_20_3] = 47,
                [ProtocolVersion.MINECRAFT_1_19] = 46
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:heightmap",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 51,
                [ProtocolVersion.MINECRAFT_1_21_5] = 50,
                [ProtocolVersion.MINECRAFT_1_20_3] = 49,
                [ProtocolVersion.MINECRAFT_1_19_4] = 47
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:uuid",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 61,
                [ProtocolVersion.MINECRAFT_1_21_6] = 56,
                [ProtocolVersion.MINECRAFT_1_21_5] = 54,
                [ProtocolVersion.MINECRAFT_1_20_5] = 53,
                [ProtocolVersion.MINECRAFT_1_20_3] = 48,
                [ProtocolVersion.MINECRAFT_1_19_4] = 48,
                [ProtocolVersion.MINECRAFT_1_19] = 47
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:loot_table",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 52,
                [ProtocolVersion.MINECRAFT_1_21_5] = 51,
                [ProtocolVersion.MINECRAFT_1_20_5] = 50
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:loot_predicate",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 53,
                [ProtocolVersion.MINECRAFT_1_21_5] = 52,
                [ProtocolVersion.MINECRAFT_1_20_5] = 51
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:loot_modifier",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 54,
                [ProtocolVersion.MINECRAFT_1_21_5] = 53,
                [ProtocolVersion.MINECRAFT_1_20_5] = 52
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:hex_color",
            new()
            {
                [ProtocolVersion.MINECRAFT_1_21_6] = 17
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:dialog",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 58,
                [ProtocolVersion.MINECRAFT_1_21_6] = 55
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:context_float_provider",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 55
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:context_int_provider",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 56
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:slot_source",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 57
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:feature",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 59
            })),
        ArgumentParserDefinition.From(new ArgumentSerializerMapping("minecraft:swing_animation",
            new()
            {
                [ProtocolVersion.MINECRAFT_26_3] = 60
            }))

        #endregion
    ];

    #endregion
}

/// <summary>
/// Associates an argument serializer and optional runtime argument type with its protocol identifier mapping.
/// </summary>
/// <param name="Serializer">The serializer used to read and write parser properties.</param>
/// <param name="ArgumentType">The concrete argument type, or <see langword="null" /> for passthrough or otherwise untyped parsers.</param>
/// <param name="Mapping">The resource identifier and numeric parser identifiers by protocol version.</param>
public record ArgumentParserDefinition(IArgumentSerializer Serializer, Type? ArgumentType, ArgumentSerializerMapping Mapping)
{
    /// <summary>
    /// Creates an untyped definition using the empty passthrough serializer.
    /// </summary>
    /// <param name="mapping">The parser identifier mapping.</param>
    /// <returns>An untyped parser definition.</returns>
    public static ArgumentParserDefinition From(ArgumentSerializerMapping mapping)
    {
        return From(EmptyArgumentPassthroughSerializer.Instance, mapping);
    }

    /// <summary>
    /// Creates an untyped definition using an explicit serializer.
    /// </summary>
    /// <param name="serializer">The serializer for parser properties.</param>
    /// <param name="mapping">The parser identifier mapping.</param>
    /// <returns>An untyped parser definition whose <see cref="ArgumentType" /> is <see langword="null" />.</returns>
    public static ArgumentParserDefinition From(IArgumentSerializer serializer, ArgumentSerializerMapping mapping)
    {
        return new ArgumentParserDefinition(serializer, ArgumentType: null, mapping);
    }

    /// <summary>
    /// Creates a typed definition using an explicit serializer.
    /// </summary>
    /// <typeparam name="TArgumentType">The runtime argument type associated with the parser.</typeparam>
    /// <param name="serializer">The serializer for parser properties.</param>
    /// <param name="mapping">The parser identifier mapping.</param>
    /// <returns>A definition whose <see cref="ArgumentType" /> is <see langword="typeof" />(<typeparamref name="TArgumentType" />).</returns>
    public static ArgumentParserDefinition From<TArgumentType>(IArgumentSerializer serializer, ArgumentSerializerMapping mapping)
    {
        return new ArgumentParserDefinition(serializer, typeof(TArgumentType), mapping);
    }
}
