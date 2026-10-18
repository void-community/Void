using System.Net;
using System.Net.Sockets;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class AgentConnectionTests
{
    [Fact]
    public async Task ConnectsToAgentOnIpv4Loopback()
    {
        using TcpListener listener = new(IPAddress.Loopback, port: 0);
        listener.Start();

        using var client = await GameRuntime.ConnectToAgentAsync(((IPEndPoint)listener.LocalEndpoint).Port, TestContext.Current.CancellationToken);

        Assert.True(client.Connected);
        Assert.Equal(AddressFamily.InterNetwork, client.Client.AddressFamily);
    }

    [Fact]
    public async Task FallsBackToIpv6LoopbackWhenAgentOnlyListensThere()
    {
        if (!Socket.OSSupportsIPv6)
            return;

        using TcpListener listener = new(IPAddress.IPv6Loopback, port: 0);
        listener.Start();

        using var client = await GameRuntime.ConnectToAgentAsync(((IPEndPoint)listener.LocalEndpoint).Port, TestContext.Current.CancellationToken);

        Assert.True(client.Connected);
        Assert.Equal(AddressFamily.InterNetworkV6, client.Client.AddressFamily);
    }

    [Fact]
    public async Task ReportsRefusalWhenNoLoopbackListens()
    {
        using TcpListener listener = new(IPAddress.Loopback, port: 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Assert.ThrowsAsync<SocketException>(() => GameRuntime.ConnectToAgentAsync(port, TestContext.Current.CancellationToken));
    }
}
