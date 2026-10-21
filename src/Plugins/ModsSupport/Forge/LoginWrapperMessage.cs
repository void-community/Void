using System.Text;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;

namespace Void.Proxy.Plugins.ModsSupport.Forge;

/// <summary>A message Forge carries in a login plugin request or answer on its <see cref="WrapperChannel"/>.</summary>
/// <param name="Channel">The channel the message is for, such as <c>fml:handshake</c>.</param>
/// <param name="Id">The message's id on its channel.</param>
/// <param name="Index">The request's index that NeoForge for Minecraft 1.20.2 repeats at the start of its payloads, or <see langword="null"/> for any other payload.</param>
public record LoginWrapperMessage(string Channel, int Id, int? Index)
{
    public const string WrapperChannel = "fml:loginwrapper";

    // The channel as a payload writes it, after its length.
    private static readonly byte[] EncodedWrapperChannel = [(byte)WrapperChannel.Length, .. Encoding.UTF8.GetBytes(WrapperChannel)];

    /// <summary>Reads the message a login wrapper payload carries.</summary>
    /// <exception cref="InvalidDataException">The payload holds more than one message.</exception>
    public static LoginWrapperMessage Read(byte[] payload)
    {
        var buffer = new BufferSpan(payload);
        var index = ReadRepeatedIndex(ref buffer);

        var channel = buffer.ReadString();
        var length = buffer.ReadVarInt();
        var id = buffer.ReadVarInt();
        buffer.Read(length - id.VarIntSize());

        if (buffer.Remaining > 0)
            throw new InvalidDataException($"Extra data remaining after parsing {WrapperChannel} message.");

        return new LoginWrapperMessage(channel, id, index);
    }

    /// <summary>Gives a payload that repeats a request's index the index of another request, as NeoForge matches answers to requests by it.</summary>
    /// <returns>The payload with <paramref name="index"/>, or <paramref name="payload"/> itself when it repeats no index.</returns>
    public static byte[] WithIndex(byte[] payload, int index)
    {
        var buffer = new BufferSpan(payload);

        if (ReadRepeatedIndex(ref buffer) is null)
            return payload;

        var rest = payload.AsSpan(buffer.Position - EncodedWrapperChannel.Length);
        var result = new byte[index.VarIntSize() + rest.Length];
        var writer = new BufferSpan(result);
        writer.WriteVarInt(index);
        writer.Write(rest);

        return result;
    }

    /// <summary>
    /// Reads the request's index and channel that NeoForge for Minecraft 1.20.2 writes again at the start of its payloads. Any other payload
    /// starts with a channel name, which can't hold the length byte that follows its own first byte, so only a repeated header has the
    /// wrapper's channel there.
    /// </summary>
    /// <returns>The repeated index, or <see langword="null"/> with the buffer left where it was when the payload repeats none.</returns>
    private static int? ReadRepeatedIndex(ref BufferSpan buffer)
    {
        var start = buffer.Position;
        var index = buffer.ReadVarInt();

        if (buffer.Remaining >= EncodedWrapperChannel.Length && buffer.Read(EncodedWrapperChannel.Length).SequenceEqual(EncodedWrapperChannel))
            return index;

        buffer.Position = start;
        return null;
    }
}
