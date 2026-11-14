using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Plugins.Common.Network.Streams.Network;
using Xunit;

namespace Void.UnitTests.Network.Streams.Network;

public class SimpleNetworkStreamTests
{
    [Fact]
    public async Task WaitForDataAsync_WhenCanceled_DoesNotConsumeNextBytesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var sendingClientScope = sendingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());
        using var waitCancellationTokenSource = new CancellationTokenSource();

        var waitTask = stream.WaitForDataAsync(waitCancellationTokenSource.Token).AsTask();
        await waitCancellationTokenSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);

        byte[] expectedBytes = [1];
        await sendingClient.GetStream().WriteAsync(expectedBytes, cancellationToken);
        await stream.WaitForDataAsync(cancellationToken);

        var actualBytes = new byte[expectedBytes.Length];
        await stream.ReadExactlyAsync(actualBytes, cancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
    }

    [Fact]
    public async Task WaitForDataAsync_WithClosedPeer_ReturnsAndReadTellsClosedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());

        var waitTask = stream.WaitForDataAsync(cancellationToken).AsTask();
        sendingClient.Dispose();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        await Assert.ThrowsAsync<StreamClosedException>(async () => await stream.ReadExactlyAsync(new byte[1], cancellationToken));
    }

    [Fact]
    public async Task WaitForDataAsync_WithPrependedBytes_ReturnsAtOnceAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var sendingClientScope = sendingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());
        stream.PrependBuffer(new byte[] { 1 });

        await stream.WaitForDataAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task IsAlive_WithAvailableBytes_DoesNotConsumeBytesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var sendingClientScope = sendingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());
        byte[] expectedBytes = [1];

        await sendingClient.GetStream().WriteAsync(expectedBytes, cancellationToken);
        Assert.True(receivingClient.Client.Poll(TimeSpan.FromSeconds(5), SelectMode.SelectRead));
        Assert.Equal(expectedBytes.Length, receivingClient.Client.Available);

        Assert.True(stream.IsAlive);
        Assert.True(stream.IsAlive);
        Assert.Equal(expectedBytes.Length, receivingClient.Client.Available);

        var actualBytes = new byte[expectedBytes.Length];
        await stream.ReadExactlyAsync(actualBytes, cancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
    }

    [Fact]
    public async Task IsAlive_WithPrependedBytesAndClosedPeer_RemainsAliveUntilBytesAreReadAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());
        byte[] expectedBytes = [5, 6, 7, 8];
        stream.PrependBuffer(expectedBytes);

        sendingClient.Dispose();
        Assert.True(receivingClient.Client.Poll(TimeSpan.FromSeconds(5), SelectMode.SelectRead));
        Assert.True(stream.IsAlive);

        var actualBytes = new byte[expectedBytes.Length];
        await stream.ReadExactlyAsync(actualBytes, cancellationToken);
        Assert.Equal(expectedBytes, actualBytes);
        Assert.False(stream.IsAlive);
    }

    [Fact]
    public async Task IsAlive_WithClosedPeerAndNoBufferedBytes_ReturnsFalseAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (receivingClient, sendingClient) = await CreateConnectedClientsAsync(cancellationToken);
        using var receivingClientScope = receivingClient;
        using var stream = new SimpleNetworkStream(receivingClient.GetStream());

        sendingClient.Dispose();
        Assert.True(receivingClient.Client.Poll(TimeSpan.FromSeconds(5), SelectMode.SelectRead));

        Assert.False(stream.IsAlive);
    }

    private static async Task<(TcpClient ReceivingClient, TcpClient SendingClient)> CreateConnectedClientsAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var acceptClientTask = listener.AcceptTcpClientAsync(cancellationToken);
        var sendingClient = new TcpClient(AddressFamily.InterNetwork);

        try
        {
            await sendingClient.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cancellationToken);
            return (await acceptClientTask, sendingClient);
        }
        catch
        {
            sendingClient.Dispose();
            throw;
        }
    }
}
