using System.Net.Sockets;
using Void.Proxy.Api.Network;
using Void.Proxy.Api.Network.Exceptions;
using Void.Proxy.Api.Network.Streams.Manual.Network;

namespace Void.Proxy.Plugins.Common.Network.Streams.Network;

public class SimpleNetworkStream(NetworkStream baseStream, TimeSpan writeTimeout, TimeSpan readTimeout) : INetworkStream
{
    private Memory<byte> _nextBuffer = Memory<byte>.Empty;
    private bool _isClosed;

    public NetworkStream BaseStream => baseStream;
    public TimeSpan WriteTimeout => writeTimeout;
    public TimeSpan ReadTimeout => readTimeout;
    public bool CanRead => BaseStream.CanRead;
    public bool CanWrite => BaseStream.CanWrite;
    public bool IsAlive => IsAliveComplex();

    public SimpleNetworkStream(NetworkStream baseStream) : this(baseStream, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15))
    {
        // Intentionally left blank
    }

    public void PrependBuffer(Memory<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfZero(buffer.Length, nameof(buffer));
        _nextBuffer = _nextBuffer.Length > 0 ? ((byte[])[.. buffer.ToArray(), .. _nextBuffer.ToArray()]).AsMemory() : buffer; // TODO do not allocate
    }

    public async ValueTask WaitForDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // A wake that finds nothing to read, such as when another reader of the socket took what woke it, waits again, since the read
            // that follows can no longer be canceled. A closed connection ends the wait, and the read then tells it is closed.
            while (_nextBuffer.Length == 0 && baseStream.Socket.Available == 0)
            {
                await baseStream.Socket.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, cancellationToken);

                if (baseStream.Socket.Available == 0 && baseStream.Socket.Poll(0, SelectMode.SelectRead))
                    return;
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            throw new StreamClosedException();
        }
    }

    public int Read(Span<byte> span)
    {
        if (_nextBuffer is not { Length: > 0 })
        {
            try
            {
                var bytesRead = baseStream.Read(span);
                return bytesRead > 0 ? bytesRead : throw new StreamClosedException();
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException { InnerException: SocketException } or ObjectDisposedException)
            {
                throw new StreamClosedException();
            }
        }

        var length = Math.Min(_nextBuffer.Length, span.Length);
        _nextBuffer.Span[..length].CopyTo(span);
        _nextBuffer = _nextBuffer[length..];
        return length;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> memory, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithTimeout(async (linkedCancellationToken) =>
        {
            if (_nextBuffer is not { Length: > 0 })
            {
                try
                {
                    await baseStream.Socket.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, linkedCancellationToken);
                    
                    var bytesRead = await baseStream.ReadAsync(memory, CancellationToken.None);
                    return bytesRead > 0 ? bytesRead : throw new StreamClosedException();
                }
                catch (Exception exception) when (exception is EndOfStreamException or SocketException or IOException { InnerException: SocketException } or ObjectDisposedException)
                {
                    throw new StreamClosedException();
                }
            }

            var length = Math.Min(_nextBuffer.Length, memory.Length);
            _nextBuffer[..length].CopyTo(memory);
            _nextBuffer = _nextBuffer[length..];
            return length;
        }, Operation.Read, cancellationToken);
    }

    public void ReadExactly(Span<byte> span)
    {
        if (_nextBuffer.Length >= span.Length)
        {
            _nextBuffer.Span[..span.Length].CopyTo(span);
            _nextBuffer = _nextBuffer[span.Length..];
        }
        else
        {
            try
            {
                var length = _nextBuffer.Length;
                _nextBuffer.Span.CopyTo(span[..length]);
                _nextBuffer = Memory<byte>.Empty;
                baseStream.ReadExactly(span[length..]);
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException { InnerException: SocketException } or ObjectDisposedException)
            {
                throw new StreamClosedException();
            }
        }
    }

    public async ValueTask ReadExactlyAsync(Memory<byte> memory, CancellationToken cancellationToken = default)
    {
        await ExecuteWithTimeout(async linkedCancellationToken =>
        {
            if (_nextBuffer.Length >= memory.Length)
            {
                _nextBuffer[..memory.Length].CopyTo(memory);
                _nextBuffer = _nextBuffer[memory.Length..];
            }
            else
            {
                var length = _nextBuffer.Length;
                _nextBuffer.CopyTo(memory[..length]);
                _nextBuffer = Memory<byte>.Empty;

                var remainingMemory = memory[length..];

                while (remainingMemory.Length > 0)
                {
                    try
                    {
                        await baseStream.Socket.ReceiveAsync(Memory<byte>.Empty, SocketFlags.None, linkedCancellationToken);
                        
                        var bytesRead = await baseStream.ReadAsync(remainingMemory, CancellationToken.None);
                        
                        if (bytesRead is 0)
                            throw new StreamClosedException();

                        remainingMemory = remainingMemory[bytesRead..];
                    }
                    catch (Exception exception) when (exception is EndOfStreamException or SocketException or IOException { InnerException: SocketException } or ObjectDisposedException)
                    {
                        throw new StreamClosedException();
                    }
                }
            }
        }, Operation.Read, cancellationToken);
    }

    public void Write(ReadOnlySpan<byte> span)
    {
        try
        {
            baseStream.Write(span);
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException { InnerException: SocketException } or ObjectDisposedException)
        {
            throw new StreamClosedException();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> memory, CancellationToken cancellationToken = default)
    {
        await ExecuteWithTimeout(async (linkedCancellationToken) =>
        {
            try
            {
                await baseStream.WriteAsync(memory, linkedCancellationToken);
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException { InnerException: SocketException } or ObjectDisposedException)
            {
                throw new StreamClosedException();
            }
        }, Operation.Write, cancellationToken);
    }

    public void Flush()
    {
        baseStream.Flush();
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await baseStream.FlushAsync(cancellationToken);
    }

    public void Close()
    {
        _isClosed = true;
        baseStream.Close();
    }

    public void Dispose()
    {
        _isClosed = true;
        baseStream.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        _isClosed = true;
        await baseStream.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected bool IsAliveComplex()
    {
        if (_isClosed)
            return false;

        if (_nextBuffer.Length > 0)
            return true;

        try
        {
            var isPollSuccessful = baseStream.Socket.Poll(0, SelectMode.SelectRead);

            if (!isPollSuccessful || baseStream.Socket.Available > 0)
                return true;

            // A readable socket with nothing to read has ended, unless another reader took what made it readable meanwhile, after which it
            // isn't readable any more
            return !baseStream.Socket.Poll(0, SelectMode.SelectRead) || baseStream.Socket.Available > 0;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    private async ValueTask ExecuteWithTimeout(Func<CancellationToken, ValueTask> operationFunction, Operation operation, CancellationToken cancellationToken)
    {
        await ExecuteWithTimeout(async linkedCancellationToken =>
        {
            await operationFunction(linkedCancellationToken);
            return 0;
        }, operation, cancellationToken);
    }

    private async ValueTask<TResult> ExecuteWithTimeout<TResult>(Func<CancellationToken, ValueTask<TResult>> operationFunction, Operation operation, CancellationToken cancellationToken)
    {
        using var operationCancellationTokenSource = new CancellationTokenSource();
        operationCancellationTokenSource.CancelAfter(operation is Operation.Read ? readTimeout : writeTimeout);

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationCancellationTokenSource.Token);

        try
        {
            return await operationFunction(linkedCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (operationCancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new StreamTimeoutException(operation);
        }
    }
}
