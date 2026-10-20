using System;
using System.IO;
using Void.Minecraft.Buffers;
using Void.Minecraft.Network.Registries.Transformations.Properties;
using Void.Minecraft.Profiles;
using Void.Proxy.Plugins.Common.Network.Messages.Binary;
using Void.Proxy.Plugins.Common.Network.Registries.Transformations.Mappings;
using Xunit;

namespace Void.UnitTests.Network;

public class PacketPropertyTests
{
    [Fact]
    public void FloatPropertyEncodesBigEndianAndReadsBack()
    {
        // Issue #1605: FloatProperty wrote into a read-only buffer, so FromPrimitive and Read threw.
        var property = FloatProperty.FromPrimitive(1f);

        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, property.Value.ToArray());
        Assert.Equal(1f, property.AsPrimitive);

        var buffer = new MinecraftBuffer(new ReadOnlySpan<byte>([0x3F, 0x80, 0x00, 0x00]));
        Assert.Equal(1f, FloatProperty.Read(ref buffer).AsPrimitive);
    }

    [Fact]
    public void PrimitivePropertiesRoundTrip()
    {
        Assert.True(RoundTrip(BoolProperty.FromPrimitive(true)).AsPrimitive);
        Assert.Equal(byte.MaxValue, RoundTrip(ByteProperty.FromPrimitive(byte.MaxValue)).AsPrimitive);
        Assert.Equal(short.MinValue, RoundTrip(ShortProperty.FromPrimitive(short.MinValue)).AsPrimitive);
        Assert.Equal(-123_456, RoundTrip(IntProperty.FromPrimitive(-123_456)).AsPrimitive);
        Assert.Equal(long.MaxValue, RoundTrip(LongProperty.FromPrimitive(long.MaxValue)).AsPrimitive);
        Assert.Equal(-90.5f, RoundTrip(FloatProperty.FromPrimitive(-90.5f)).AsPrimitive);
        Assert.Equal(Math.PI, RoundTrip(DoubleProperty.FromPrimitive(Math.PI)).AsPrimitive);
        Assert.Equal(int.MinValue, RoundTrip(VarIntProperty.FromPrimitive(int.MinValue)).AsPrimitive);
        Assert.Equal(long.MinValue, RoundTrip(VarLongProperty.FromPrimitive(long.MinValue)).AsPrimitive);
        Assert.Equal("Void", RoundTrip(StringProperty.FromPrimitive("Void")).AsPrimitive);

        var uuid = Uuid.Parse("069a79f4-44e9-4726-a5be-fca90e38aaf5");
        Assert.Equal(uuid, RoundTrip(UuidProperty.FromUuid(uuid)).AsUuid);
    }

    [Fact]
    public void PassthroughKeepsFloatFieldsBitForBit()
    {
        // The transformation from the issue: yaw and pitch passed through a packet wrapper.
        float[] values = [123.25f, -45.5f, float.NaN];
        using var input = new MemoryStream();
        var inputBuffer = new MinecraftBuffer(input);

        foreach (float value in values)
            inputBuffer.WriteFloat(value);

        input.Position = 0;
        var wrapper = new MinecraftBinaryPacketWrapper(new MinecraftBinaryPacket(0, input));

        foreach (float value in values)
            Assert.Equal(BitConverter.SingleToInt32Bits(value), BitConverter.SingleToInt32Bits(wrapper.Passthrough<FloatProperty>().AsPrimitive));

        wrapper.Reset();
        using var output = new MemoryStream();
        var outputBuffer = new MinecraftBuffer(output);
        wrapper.WriteProcessedValues(ref outputBuffer);

        Assert.Equal(input.ToArray(), output.ToArray());
    }

    private static TPacketProperty RoundTrip<TPacketProperty>(TPacketProperty property) where TPacketProperty : IPacketProperty<TPacketProperty>
    {
        var bytes = new byte[64];
        var writer = new MinecraftBuffer(bytes.AsSpan());
        property.Write(ref writer);

        var reader = new MinecraftBuffer(new ReadOnlySpan<byte>(bytes, 0, (int)writer.Position));
        var read = TPacketProperty.Read(ref reader);

        Assert.Equal(writer.Position, reader.Position);
        return read;
    }
}
