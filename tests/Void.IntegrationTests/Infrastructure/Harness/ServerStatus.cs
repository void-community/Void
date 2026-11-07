using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Void.IntegrationTests.Infrastructure.Exceptions;
using Void.Minecraft.Buffers;
using Void.Minecraft.Network;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>Asks a server for its status, as the server list of a game does.</summary>
public static class ServerStatus
{
    // A status answer is a JSON string, which a server keeps to the longest string the protocol allows.
    private const int MaximumPacketLength = 32767 * 3 + 8;

    /// <summary>Returns the JSON status that the server on the loopback <paramref name="port"/> answers to a game of <paramref name="protocolVersion"/>.</summary>
    public static async Task<string> RequestAsync(int port, ProtocolVersion protocolVersion, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);

        await using var stream = client.GetStream();
        await stream.WriteAsync(CreateHandshakePacket(protocolVersion.Value, IPAddress.Loopback.ToString(), checked((ushort)port)), cancellationToken);
        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, cancellationToken);

        var length = await ReadVarIntAsync(stream, cancellationToken);

        if (length is <= 0 or > MaximumPacketLength)
            throw new IntegrationTestException($"Server on port {port} answered a status packet of {length} bytes");

        var packet = new byte[length];
        await stream.ReadExactlyAsync(packet, cancellationToken);

        return ReadStatusResponse(packet);
    }

    private static string ReadStatusResponse(byte[] packet)
    {
        var buffer = new MinecraftBuffer(packet.AsSpan());

        if (buffer.ReadVarInt() is var packetId and not 0)
            throw new IntegrationTestException($"Server answered the status request with packet {packetId}");

        return buffer.ReadString();
    }

    private static byte[] CreateHandshakePacket(int protocolVersion, string serverAddress, ushort serverPort)
    {
        using var packetDataStream = new MemoryStream();
        var packetDataBuffer = new MinecraftBuffer(packetDataStream);
        packetDataBuffer.WriteVarInt(0);
        packetDataBuffer.WriteVarInt(protocolVersion);
        packetDataBuffer.WriteString(serverAddress);
        packetDataBuffer.WriteUnsignedShort(serverPort);
        packetDataBuffer.WriteVarInt(1);

        var packetData = packetDataStream.ToArray();
        using var packetStream = new MemoryStream();
        var packetBuffer = new MinecraftBuffer(packetStream);
        packetBuffer.WriteVarInt(packetData.Length);
        packetStream.Write(packetData);

        return packetStream.ToArray();
    }

    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
    {
        var value = 0;
        var buffer = new byte[1];

        for (var position = 0; position < 35; position += 7)
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            value |= (buffer[0] & 0x7F) << position;

            if ((buffer[0] & 0x80) is 0)
                return value;
        }

        throw new IntegrationTestException("Server answered a VarInt longer than 5 bytes");
    }
}
