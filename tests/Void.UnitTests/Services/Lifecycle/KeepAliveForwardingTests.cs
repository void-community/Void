using System;
using System.Reflection;
using Void.Proxy.Api.Links;
using Void.Proxy.Plugins.Common.Services.Lifecycle;
using Xunit;

namespace Void.UnitTests.Services.Lifecycle;

public class KeepAliveForwardingTests
{
    [Fact]
    public void ForwardsAnAnswerOnceAndOnlyToTheLinkThatAsked()
    {
        var forwarding = new KeepAliveForwarding();
        var oldLink = CreateLink();
        var newLink = CreateLink();
        forwarding.Remember(oldLink, 7);

        Assert.False(forwarding.TryAccept(newLink, 7));
        Assert.True(forwarding.TryAccept(oldLink, 7));
        Assert.False(forwarding.TryAccept(oldLink, 7));
    }

    [Fact]
    public void ForgetsTheOldestRequestsOfAPlayerThatNeverAnswers()
    {
        var forwarding = new KeepAliveForwarding();
        var link = CreateLink();

        for (long id = 0; id <= KeepAliveForwarding.MaximumPendingRequests; id++)
            forwarding.Remember(link, id);

        Assert.False(forwarding.TryAccept(link, 0));
        Assert.True(forwarding.TryAccept(link, 1));
        Assert.True(forwarding.TryAccept(link, KeepAliveForwarding.MaximumPendingRequests));
    }

    internal static ILink CreateLink() => DispatchProxy.Create<ILink, LinkStandIn>();

    // Forwarding only compares links by reference, so a stand-in never needs to do anything.
    public class LinkStandIn : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}
