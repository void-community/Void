using Void.Minecraft;
using Void.Minecraft.Network;
using Xunit;

namespace Void.UnitTests.Network;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.20.4", "1.20.5")]
    [InlineData("1.20", "1.20.1")]
    [InlineData("1.9.4", "1.10")]
    [InlineData("1.21.11", "26.1")]
    public void ComparesLikeCompareTo(string earlier, string later)
    {
        ReleaseVersion left = earlier;
        ReleaseVersion right = later;

        Assert.True(left < right);
        Assert.True(left <= right);
        Assert.False(left > right);
        Assert.False(left >= right);
        Assert.True(right > left);
        Assert.True(right >= left);
        Assert.Equal(left.CompareTo(right) < 0, left < right);
    }

    [Fact]
    public void EqualVersionsAreNeitherEarlierNorLater()
    {
        ReleaseVersion left = "1.21";
        ReleaseVersion right = new(1, 21, Patch: 0);

        Assert.False(left < right);
        Assert.False(left > right);
        Assert.True(left <= right);
        Assert.True(left >= right);
    }

    [Fact]
    public void NullSortsBeforeAnyVersion()
    {
        ReleaseVersion version = "1.7.2";
        ReleaseVersion? missing = null;

        Assert.True(missing < version);
        Assert.True(version > missing);
        Assert.False(missing > version);
        Assert.True(missing <= missing);
        Assert.False(missing < missing);
    }

    [Fact]
    public void ComparesAVersionUnknownToTheProtocolTable()
    {
        // A modpack's Minecraft version can be newer than any protocol the library knows, but still compares with the table's releases.
        Assert.True(ReleaseVersion.TryParse("99.1", out var release));
        Assert.True(release > ProtocolVersion.Latest.LastRelease);
        Assert.False(release < ProtocolVersion.MINECRAFT_1_20_5.FirstRelease);
    }
}
