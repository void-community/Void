using System;
using Void.Proxy.Plugins.ModsSupport.SlowJoin;
using Void.Proxy.Plugins.ModsSupport.SlowJoin.Services;
using Void.UnitTests.Services.Lifecycle;
using Xunit;

namespace Void.UnitTests.Plugins;

public class SlowJoinGraceTests
{
    private static readonly DateTimeOffset Joined = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ProbesOnceUntilThePlayerAnswersTheProbe()
    {
        var grace = new SlowJoinGrace(KeepAliveForwardingTests.CreateLink(), TimeSpan.FromMinutes(3));

        Assert.True(grace.TryStartProbe(5, Joined));
        Assert.False(grace.TryStartProbe(6, Joined.AddSeconds(15)));
        Assert.False(grace.TryEnd(6, Joined.AddSeconds(20), out _));
        Assert.False(grace.IsOver);

        Assert.True(grace.TryEnd(5, Joined.AddSeconds(42), out var waited));
        Assert.Equal(TimeSpan.FromSeconds(42), waited);
        Assert.True(grace.IsOver);
        Assert.False(grace.TryStartProbe(7, Joined.AddSeconds(45)));
    }

    [Fact]
    public void TimesOutOnlyWhenTheProbeWaitedForTheWholeTimeout()
    {
        var grace = new SlowJoinGrace(KeepAliveForwardingTests.CreateLink(), TimeSpan.FromMinutes(3));

        Assert.False(grace.TryTimeOut(Joined.AddMinutes(10)));
        Assert.True(grace.TryStartProbe(5, Joined));
        Assert.False(grace.TryTimeOut(Joined.AddMinutes(3).AddMilliseconds(-1)));
        Assert.True(grace.TryTimeOut(Joined.AddMinutes(3)));
        Assert.False(grace.TryEnd(5, Joined.AddMinutes(4), out _));
    }

    [Fact]
    public void TheProgramArgumentWinsOverTheSetting()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), SlowJoinService.GetTimeout(argumentSeconds: null, new Settings().FirstKeepAliveTimeout));
        Assert.Equal(TimeSpan.FromSeconds(300), SlowJoinService.GetTimeout(argumentSeconds: 300, settingSeconds: 180));
        Assert.Equal(TimeSpan.FromSeconds(1), SlowJoinService.GetTimeout(argumentSeconds: 0, settingSeconds: 180));
    }
}
