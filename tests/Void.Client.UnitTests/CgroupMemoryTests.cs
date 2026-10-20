using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class CgroupMemoryTests
{
    [Fact]
    public void ParsesOutOfMemoryKillCount()
    {
        const string content = "low 0\nhigh 0\noom 3\noom_kill 2\noom_group_kill 0\n";

        Assert.Equal(2, CgroupMemory.ParseOutOfMemoryKillCount(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("oom 1")]
    [InlineData("oom_kill invalid")]
    public void MissingOrInvalidOutOfMemoryKillCountReturnsNull(string content)
    {
        Assert.Null(CgroupMemory.ParseOutOfMemoryKillCount(content));
    }

    [Theory]
    [InlineData("2147483648\n", 2048L)]
    [InlineData("max\n", null)]
    [InlineData("", null)]
    public void ParsesMemoryLimitInMebibytes(string content, long? limitMb)
    {
        Assert.Equal(limitMb, CgroupMemory.ParseMemoryLimitMb(content));
    }
}
