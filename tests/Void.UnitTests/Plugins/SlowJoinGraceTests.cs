using System;
using System.Threading.Tasks;
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
        var grace = CreateGrace();

        Assert.True(grace.TryStartProbe(5, Joined, out _));
        Assert.False(grace.TryStartProbe(6, Joined.AddSeconds(15), out _));
        Assert.False(grace.TryAnswer(6, Joined.AddSeconds(20), out _, out _));

        Assert.True(grace.TryAnswer(5, Joined.AddSeconds(42), out var waited, out int answeredPromptly));
        Assert.Equal(TimeSpan.FromSeconds(42), waited);
        Assert.Equal(0, answeredPromptly);
        Assert.False(grace.TryAnswer(5, Joined.AddSeconds(43), out _, out _));
        Assert.True(grace.TryStartProbe(7, Joined.AddSeconds(45), out _));
    }

    [Fact]
    public void EndsOnlyAfterEnoughPromptAnswersInARow()
    {
        var grace = CreateGrace();

        // A slow answer starts the run of prompt answers over.
        Answer(grace, probeId: 1, sentAfterSeconds: 15, waitedSeconds: 0.01, expectedRun: 1);
        Answer(grace, probeId: 2, sentAfterSeconds: 30, waitedSeconds: 40, expectedRun: 0);
        Answer(grace, probeId: 3, sentAfterSeconds: 75, waitedSeconds: 0.01, expectedRun: 1);
        Answer(grace, probeId: 4, sentAfterSeconds: 90, waitedSeconds: 5, expectedRun: 2);
        Assert.False(grace.IsOver);

        Answer(grace, probeId: 5, sentAfterSeconds: 105, waitedSeconds: 0.01, expectedRun: 3);
        Assert.True(grace.IsOver);
        Assert.False(grace.TryStartProbe(6, Joined.AddSeconds(120), out _));
    }

    [Fact]
    public void KeepsAnsweringWhileJeiLoadsBetweenPromptAnswers()
    {
        // The timeline from issue #1596: the first probe is answered at once between two JEI steps that block the client for 43 and 47 seconds.
        var grace = CreateGrace();

        Answer(grace, probeId: 1, sentAfterSeconds: 27, waitedSeconds: 0.005, expectedRun: 1);
        Answer(grace, probeId: 2, sentAfterSeconds: 42, waitedSeconds: 38, expectedRun: 0);
        Answer(grace, probeId: 3, sentAfterSeconds: 87, waitedSeconds: 40, expectedRun: 0);
        Assert.False(grace.IsOver);

        Answer(grace, probeId: 4, sentAfterSeconds: 132, waitedSeconds: 0.005, expectedRun: 1);
        Answer(grace, probeId: 5, sentAfterSeconds: 147, waitedSeconds: 0.005, expectedRun: 2);
        Answer(grace, probeId: 6, sentAfterSeconds: 162, waitedSeconds: 0.005, expectedRun: 3);
        Assert.True(grace.IsOver);
    }

    [Fact]
    public void TimesOutOnlyWhenAProbeWaitedForTheWholeTimeout()
    {
        var grace = CreateGrace();

        Assert.False(grace.TryTimeOut(Joined.AddMinutes(10)));
        Answer(grace, probeId: 5, sentAfterSeconds: 0, waitedSeconds: 1, expectedRun: 1);
        Assert.False(grace.TryTimeOut(Joined.AddMinutes(10)));

        Assert.True(grace.TryStartProbe(6, Joined.AddMinutes(1), out _));
        Assert.False(grace.TryTimeOut(Joined.AddMinutes(4).AddMilliseconds(-1)));
        Assert.True(grace.TryTimeOut(Joined.AddMinutes(4)));
        Assert.False(grace.TryAnswer(6, Joined.AddMinutes(5), out _, out _));
    }

    [Fact]
    public void EveryWayAProbeEndsSettlesItsTimeout()
    {
        // A timeout still waiting after its probe would keep the link, and with it the protocol plugin, from unloading.
        var grace = CreateGrace();

        Assert.True(grace.TryStartProbe(1, Joined, out var answered));
        Assert.False(answered.IsCancellationRequested);
        Assert.True(grace.TryAnswer(1, Joined.AddMilliseconds(1), out _, out _));
        Assert.True(answered.IsCancellationRequested);

        Assert.True(grace.TryStartProbe(2, Joined.AddSeconds(15), out var timedOut));
        Assert.True(grace.TryTimeOut(Joined.AddMinutes(4)));
        Assert.True(timedOut.IsCancellationRequested);

        var ended = CreateGrace();
        Assert.True(ended.TryStartProbe(3, Joined, out var outstanding));
        ended.End();
        Assert.True(outstanding.IsCancellationRequested);
    }

    [Fact]
    public void DefaultsLastThroughLoadingThatStartsAfterTheFirstMinute()
    {
        // Issue #1624: All the Mods 10 answers promptly for 45 s after a join, and then JEI blocks its render thread for 44 s.
        var settings = new Settings();
        var grace = new SlowJoinGrace(KeepAliveForwardingTests.CreateLink(), TimeSpan.FromSeconds(settings.FirstKeepAliveTimeout), settings.PromptAnswers, TimeSpan.FromSeconds(settings.PromptAnswerTime));

        Answer(grace, probeId: 1, sentAfterSeconds: 15, waitedSeconds: 0.03, expectedRun: 1);
        Answer(grace, probeId: 2, sentAfterSeconds: 30, waitedSeconds: 0.03, expectedRun: 2);
        Answer(grace, probeId: 3, sentAfterSeconds: 45, waitedSeconds: 0.03, expectedRun: 3);
        Assert.False(grace.IsOver);

        Answer(grace, probeId: 4, sentAfterSeconds: 60, waitedSeconds: 28, expectedRun: 0);
        Assert.False(grace.IsOver);
    }

    [Fact]
    public void AnAnswerBeforeTheTimeoutStartsLeavesNothingWaiting()
    {
        // The player can answer before the proxy has finished sending the probe, so the timeout may only start afterwards.
        var grace = CreateGrace();

        Assert.True(grace.TryStartProbe(1, Joined, out var settled));
        Assert.True(grace.TryAnswer(1, Joined.AddMilliseconds(1), out _, out _));
        Assert.True(Task.Delay(grace.Timeout, settled).IsCanceled);
    }

    [Fact]
    public void TheProgramArgumentWinsOverTheSetting()
    {
        var settings = new Settings();

        Assert.Equal(TimeSpan.FromSeconds(180), SlowJoinService.GetTimeout(argumentSeconds: null, settings.FirstKeepAliveTimeout));
        Assert.Equal(TimeSpan.FromSeconds(300), SlowJoinService.GetTimeout(argumentSeconds: 300, settingSeconds: 180));
        Assert.Equal(TimeSpan.FromSeconds(1), SlowJoinService.GetTimeout(argumentSeconds: 0, settingSeconds: 180));
        Assert.Equal(TimeSpan.FromSeconds(5), SlowJoinService.GetTimeout(argumentSeconds: null, settings.PromptAnswerTime));
        Assert.Equal(8, SlowJoinService.GetCount(argument: null, settings.PromptAnswers));
        Assert.Equal(6, SlowJoinService.GetCount(argument: 6, setting: 3));
        Assert.Equal(1, SlowJoinService.GetCount(argument: -2, setting: 3));
    }

    private static SlowJoinGrace CreateGrace()
    {
        return new SlowJoinGrace(KeepAliveForwardingTests.CreateLink(), TimeSpan.FromMinutes(3), promptAnswers: 3, TimeSpan.FromSeconds(5));
    }

    private static void Answer(SlowJoinGrace grace, long probeId, double sentAfterSeconds, double waitedSeconds, int expectedRun)
    {
        var sentAt = Joined.AddSeconds(sentAfterSeconds);

        Assert.True(grace.TryStartProbe(probeId, sentAt, out _));
        Assert.True(grace.TryAnswer(probeId, sentAt.AddSeconds(waitedSeconds), out _, out int answeredPromptly));
        Assert.Equal(expectedRun, answeredPromptly);
    }
}
