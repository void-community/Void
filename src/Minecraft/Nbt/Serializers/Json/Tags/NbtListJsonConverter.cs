using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Void.Minecraft.Nbt.Serializers.Json.Adapter;
using Void.Minecraft.Nbt.Tags;

namespace Void.Minecraft.Nbt.Serializers.Json.Tags;

/// <summary>Converts between NBT lists and JSON arrays.</summary>
/// <remarks>
/// Numeric elements are widened to a common NBT numeric type during deserialization. An array whose elements have different types becomes a
/// list of compounds, with each element that isn't a compound under an empty key, as Minecraft writes such lists since 1.20.3, so a text
/// component's arguments can mix strings and components. As in Minecraft, serialization writes any compound that holds only an empty key as its
/// value, even in a list that held only compounds.
/// </remarks>
public class NbtListJsonConverter : JsonConverter<NbtList>
{
    private const string WrappedElementKey = "";

    /// <inheritdoc/>
    public override NbtList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = new List<NbtTag>();

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndArray)
                break;

            var tag = JsonSerializer.Deserialize<NbtTag>(ref reader, options) ?? throw new JsonException("Expected tag, but got null.");
            values.Add(tag);
        }

        if (values.Count is 0)
            return new NbtList(values, NbtTagType.End);

        NbtTagNumberAdapter.AlignNumbers(values);

        if (values.Any(value => value.Type != values[0].Type))
            return new NbtList(values.Select(Wrap).ToList(), NbtTagType.Compound);

        return new NbtList(values, values[0].Type);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, NbtList tag, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var value in tag.Data)
            JsonSerializer.Serialize(writer, Unwrap(value), options);

        writer.WriteEndArray();
    }

    // A compound that holds only an empty key is wrapped too, so it isn't unwrapped into its value.
    private static NbtCompound Wrap(NbtTag tag)
    {
        return tag is NbtCompound compound && !IsWrapped(compound) ? compound : new NbtCompound { [WrappedElementKey] = tag };
    }

    private static NbtTag Unwrap(NbtTag tag)
    {
        return tag is NbtCompound compound && IsWrapped(compound) ? compound[WrappedElementKey]! : tag;
    }

    private static bool IsWrapped(NbtCompound compound)
    {
        return compound.Fields.Count is 1 && compound.ContainsKey(WrappedElementKey);
    }
}
