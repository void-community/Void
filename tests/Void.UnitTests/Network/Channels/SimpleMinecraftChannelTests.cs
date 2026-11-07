using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Network.Streams;
using Void.Proxy.Api.Network.Streams.Manual;
using Void.Proxy.Plugins.Common.Network.Channels;
using Void.Proxy.Plugins.Common.Network.Messages.Binary;
using Void.Proxy.Plugins.Common.Network.Packets.Clientbound;
using Void.Proxy.Plugins.Common.Network.Streams.Compression;
using Void.Proxy.Plugins.Common.Network.Streams.Packet;
using Xunit;
using static Void.UnitTests.Players.PlayerServiceDisconnectTests;

namespace Void.UnitTests.Network.Channels;

public class SimpleMinecraftChannelTests
{
    [Fact]
    public async Task MessageWhoseWriteIsCancelledHalfwayIsWrittenWhole()
    {
        // A message goes out in several writes, its lengths first. One cut short leaves the other side reading the rest of the connection
        // from the middle of a message.
        var written = new List<byte>();
        var stall = new TaskCompletionSource();
        var writes = 0;

        var network = StandIn.Create<IStallingStream>((method, arguments) => method.Name switch
        {
            "get_CanWrite" => true,
            nameof(IManualStream.WriteAsync) when arguments is [ReadOnlyMemory<byte> memory, CancellationToken cancellationToken] => WriteAsync(memory.ToArray(), cancellationToken),
            _ => null
        });

        var channel = new SimpleMinecraftChannel(new IonicZlibCompressionMessageStream { BaseStream = network });
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var write = channel.WriteMessageAsync(new CompleteBinaryMessage(new RecyclableMemoryStreamManager().GetStream([1, 2, 3])), cancellationTokenSource.Token).AsTask();

        while (Volatile.Read(ref writes) < 2)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cancellationTokenSource.CancelAsync();
        stall.SetResult();
        await write;

        Assert.Equal([4, 0, 1, 2, 3], written);

        async ValueTask WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            // The network takes the first write at once and waits on the next, as a socket whose buffer is full does
            if (Interlocked.Increment(ref writes) > 1)
                await stall.Task.WaitAsync(cancellationToken);

            lock (written)
                written.AddRange(bytes);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PacketNoLongerRegisteredIsUnregisteredOnlyWhileChannelIsOpen(bool open)
    {
        // A player kicked as its protocol unloads is closed before the protocol takes its packets away, while a plugin may still send it one
        var network = StandIn.Create<IStallingStream>((method, _) => method.Name is "get_CanWrite" ? open : null);
        var channel = new SimpleMinecraftChannel(new MinecraftPacketMessageStream { BaseStream = network });

        var write = channel.WriteMessageAsync(new KeepAliveRequestPacket { Id = 1 }, TestContext.Current.CancellationToken).AsTask();

        if (open)
            await Assert.ThrowsAsync<InvalidOperationException>(() => write);
        else
            await Assert.ThrowsAsync<StreamClosedException>(() => write);
    }

    public interface IStallingStream : IManualStream, IMessageStreamBase;
}
