using System.Text;
using Void.Minecraft.Buffers;
using Void.Minecraft.Buffers.Extensions;

namespace Void.Proxy.Plugins.ModsSupport.Forge;

/// <summary>A message Forge carries in a login plugin request or answer on its <see cref="WrapperChannel"/>.</summary>
/// <param name="Channel">The channel the message is for, such as <c>fml:handshake</c>.</param>
/// <param name="Id">The message's id on its channel.</param>
public record LoginWrapperMessage(string Channel, int Id)
{
    public const string WrapperChannel = "fml:loginwrapper";

    // The channel as a payload writes it, after its length.
    private static readonly byte[] EncodedWrapperChannel = [(byte)WrapperChannel.Length, .. Encoding.UTF8.GetBytes(WrapperChannel)];

    /// <summary>Reads the message a login wrapper payload carries.</summary>
    /// <exception cref="InvalidDataException">The payload holds more than one message.</exception>
    public static LoginWrapperMessage Read(byte[] payload)
    {
        var buffer = new BufferSpan(payload);
        SkipRepeatedHeader(ref buffer);

        var channel = buffer.ReadString();
        var length = buffer.ReadVarInt();
        var id = buffer.ReadVarInt();
        buffer.Read(length - id.VarIntSize());

        if (buffer.Remaining > 0)
            throw new InvalidDataException($"Extra data remaining after parsing {WrapperChannel} message.");

        return new LoginWrapperMessage(channel, id);
    }

    /// <summary>
    /// Skips the request's index and channel that NeoForge for Minecraft 1.20.2 writes again at the start of its payloads. Any other payload
    /// starts with a channel name, which can't hold the length byte that follows its own first byte, so only a repeated header has the
    /// wrapper's channel there.
    /// </summary>
    private static void SkipRepeatedHeader(ref BufferSpan buffer)
    {
        var start = buffer.Position;
        buffer.ReadVarInt();

        if (buffer.Remaining >= EncodedWrapperChannel.Length && buffer.Read(EncodedWrapperChannel.Length).SequenceEqual(EncodedWrapperChannel))
            return;

        buffer.Position = start;
    }
}
