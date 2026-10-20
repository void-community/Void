using System;
using Void.Minecraft.Buffers;

namespace Void.Minecraft.Network.Registries.Transformations.Properties;

/// <summary>Represents a big-endian IEEE 754 single-precision packet property.</summary>
/// <param name="Value">The encoded property bytes, retained without copying or validation.</param>
public record FloatProperty(ReadOnlyMemory<byte> Value) : IPacketProperty<FloatProperty>
{
    /// <summary>Gets the single-precision value decoded from the property bytes.</summary>
    public float AsPrimitive => new MinecraftBuffer(Value.Span).ReadFloat();

    /// <summary>
    /// Creates a property payload containing the big-endian Minecraft binary representation of a single-precision floating-point value.
    /// </summary>
    /// <param name="value">The <see cref="float"/> value to serialize into the property payload.</param>
    /// <returns>A <see cref="FloatProperty"/> whose <see cref="Value"/> contains the serialized value.</returns>
    public static FloatProperty FromPrimitive(float value)
    {
        // An array would bind to the read-only ReadOnlySpan<byte> constructor, so the buffer is built over a writable span.
        Span<byte> bytes = stackalloc byte[sizeof(float)];
        var buffer = new MinecraftBuffer(bytes);
        buffer.WriteFloat(value);

        return new FloatProperty(bytes.ToArray());
    }

    /// <summary>Reads one big-endian single-precision value from a buffer.</summary>
    /// <param name="buffer">The source buffer.</param>
    /// <returns>The decoded property.</returns>
    public static FloatProperty Read(ref MinecraftBuffer buffer)
    {
        return FromPrimitive(buffer.ReadFloat());
    }

    /// <summary>Writes the decoded single-precision value in big-endian order.</summary>
    /// <param name="buffer">The destination buffer.</param>
    public void Write(ref MinecraftBuffer buffer)
    {
        buffer.WriteFloat(AsPrimitive);
    }
}
