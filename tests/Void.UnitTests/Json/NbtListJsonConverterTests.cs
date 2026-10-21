using System.Linq;
using System.Text.Json.Nodes;
using Void.Minecraft.Nbt;
using Void.Minecraft.Nbt.Serializers.Json;
using Void.Minecraft.Nbt.Tags;
using Xunit;

namespace Void.UnitTests.Json;

public class NbtListJsonConverterTests
{
    [Fact]
    public void ReadsArraysOfOneTypeAsListsOfThatType()
    {
        var list = Assert.IsType<NbtList>(NbtJsonSerializer.Deserialize(JsonNode.Parse("""["a","b"]""")!));

        Assert.Equal(NbtTagType.String, list.DataType);
    }

    [Fact]
    public void ReadsArraysOfMixedTypesAsCompoundsWithOtherElementsUnderAnEmptyKey()
    {
        var list = Assert.IsType<NbtList>(NbtJsonSerializer.Deserialize(JsonNode.Parse("""[{"text":"a"},"b",{"":"c"}]""")!));
        var elements = list.Data.Cast<NbtCompound>().ToArray();

        Assert.Equal(NbtTagType.Compound, list.DataType);
        Assert.Equal("a", Assert.IsType<NbtString>(elements[0]["text"]).Value);
        Assert.Equal("b", Assert.IsType<NbtString>(elements[1][""]).Value);

        // A compound holding only an empty key is wrapped as well, so it isn't taken for a wrapped element.
        Assert.Equal("c", Assert.IsType<NbtString>(Assert.IsType<NbtCompound>(elements[2][""])[""]).Value);
    }

    [Theory]
    [InlineData("""[{"text":"a"},"b",{"":"c"}]""")]
    [InlineData("""[1,"two",3.5]""")]
    [InlineData("""[1,2,300]""")]
    [InlineData("""[]""")]
    public void WritesArraysBackAsTheyWereRead(string json)
    {
        var tag = NbtJsonSerializer.Deserialize(JsonNode.Parse(json)!);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), NbtJsonSerializer.Serialize(tag)));
    }
}
