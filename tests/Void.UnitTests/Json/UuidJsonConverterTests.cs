using System;
using System.Text.Json;
using Void.Minecraft.Profiles;
using Void.Minecraft.Profiles.Serializers;
using Void.Proxy.Utils;
using Xunit;

namespace Void.UnitTests.Json;

public class UuidJsonConverterTests
{
    private const string Dashed = "069a79f4-44e9-4726-a5be-fca90e38aaf5";
    private const string Undashed = "069a79f444e94726a5befca90e38aaf5";

    [Fact]
    public void ReadsTheProfileTheSessionServerReturns()
    {
        // The shape of Mojang's session server response for a joining player, with its UUID written without dashes.
        const string response = """
            {
              "id" : "069a79f444e94726a5befca90e38aaf5",
              "name" : "Notch",
              "properties" : [ {
                "name" : "textures",
                "value" : "e30="
              } ],
              "profileActions" : [ ]
            }
            """;

        // MojangService reads it with ReadFromJsonAsync, which uses the web defaults.
        var profile = JsonSerializer.Deserialize<GameProfile>(response, JsonSerializerOptions.Web);

        Assert.NotNull(profile);
        Assert.Equal("Notch", profile.Username);
        Assert.Equal(Uuid.Parse(Dashed), profile.Id);
    }

    [Theory]
    [InlineData($"\"{Dashed}\"")]
    [InlineData($"\"{Undashed}\"")]
    [InlineData("\"069A79F444E94726A5BEFCA90E38AAF5\"")]
    [InlineData("\"\\u0030\\u0036\\u0039a79f4-44e9-4726-a5be-fca90e38aaf5\"")]
    public void ReadsUuidsWithAndWithoutDashes(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new UuidJsonConverter() } };

        Assert.Equal(Uuid.Parse(Dashed), JsonSerializer.Deserialize<Uuid>(json, options));
    }

    [Theory]
    [InlineData($"\"{Dashed}\"")]
    [InlineData($"\"{Undashed}\"")]
    public void GuidConverterReadsGuidsWithAndWithoutDashes(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new GuidConverter() } };

        Assert.Equal(Guid.Parse(Dashed), JsonSerializer.Deserialize<Guid>(json, options));
    }

    [Fact]
    public void WritesTheDashedForm()
    {
        var options = new JsonSerializerOptions { Converters = { new UuidJsonConverter() } };

        Assert.Equal($"\"{Dashed}\"", JsonSerializer.Serialize(Uuid.Parse(Undashed), options));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"not-a-uuid\"")]
    [InlineData("42")]
    public void RejectsValuesThatAreNotUuids(string json)
    {
        var options = new JsonSerializerOptions { Converters = { new UuidJsonConverter() } };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Uuid>(json, options));
    }
}
