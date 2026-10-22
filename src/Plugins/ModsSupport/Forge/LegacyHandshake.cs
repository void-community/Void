using Void.Minecraft.Buffers;
using Void.Proxy.Plugins.ModsSupport.Forge.Packets;

namespace Void.Proxy.Plugins.ModsSupport.Forge;

/// <summary>
/// The legacy Forge handshake on <see cref="Channel"/> that a player first joined a server through: how the player answered each of the server's
/// messages, and the ids the server gave its blocks and items. Forge for Minecraft 1.7.2 can't take the reset that starts another handshake, so
/// the proxy answers later servers as the player answered that one.
/// </summary>
public class LegacyHandshake
{
    public const string Channel = "FML|HS";

    private const byte ServerHelloDiscriminator = 0;
    private const byte ClientHelloDiscriminator = 1;
    private const byte ModIdDataDiscriminator = 3;
    private const byte AcknowledgementDiscriminator = 0xFF;

    // FML protocol 2, from Forge for Minecraft 1.7.10, brought the reset.
    private const byte ResettableProtocol = 2;

    private readonly Dictionary<(byte Discriminator, byte Phase), List<PluginMessagePacket>> _answers = [];

    // Servers start with their hello, and what the player sends before it belongs to it too.
    private (byte Discriminator, byte Phase) _answeredMessage = (ServerHelloDiscriminator, 0);
    private Dictionary<string, int>? _ids;
    private bool _helloAnswered;
    private byte? _protocol;

    /// <summary>Whether the recorded handshake is one the player joined a server through, which later servers' handshakes are answered from.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Whether the player can take a handshake reset, which players whose hello names FML protocol 2 or later can.</summary>
    public bool CanReset => _protocol >= ResettableProtocol;

    /// <summary>Whether the proxy answers servers' handshakes for the player, whose hello names FML protocol 1, as the player answered the recorded one.</summary>
    public bool AnswersForPlayer => IsComplete && _protocol < ResettableProtocol;

    /// <summary>Records a message the server sent, which the player's next messages answer. A hello starts the recording again.</summary>
    public void RecordServerMessage(ReadOnlySpan<byte> data)
    {
        _answeredMessage = ReadKey(data);

        if (data is [ServerHelloDiscriminator, ..])
        {
            // A server the player didn't join may have stopped partway through its handshake
            _answers.Clear();
            _ids = null;
            _helloAnswered = false;
        }

        if (data is [ModIdDataDiscriminator, ..])
            _ids ??= ReadIds(data);
    }

    /// <summary>Records a message the player sent on the handshake channel or to register its channels.</summary>
    public void RecordPlayerMessage(string channel, ReadOnlySpan<byte> data)
    {
        if (!_answers.TryGetValue(_answeredMessage, out var answers))
            _answers[_answeredMessage] = answers = [];

        answers.Add(new PluginMessagePacket { Channel = channel, Data = data.ToArray() });

        if (channel is Channel && data is [ClientHelloDiscriminator, var protocol, ..])
        {
            _protocol = protocol;
            _helloAnswered = true;
        }
    }

    /// <summary>Completes the recording when the player joins a server whose hello it answered.</summary>
    public void Complete()
    {
        if (_helloAnswered)
            IsComplete = true;
    }

    /// <summary>The messages the player answered the same message of the recorded server with.</summary>
    public IEnumerable<PluginMessagePacket> AnswersTo(ReadOnlySpan<byte> data)
    {
        return _answers.TryGetValue(ReadKey(data), out var answers) ? answers.Select(answer => new PluginMessagePacket { Channel = answer.Channel, Data = answer.Data }) : [];
    }

    /// <summary>Whether a server message leaves the player's blocks and items as the first server numbered them, which only id data can change.</summary>
    public bool KeepsIds(ReadOnlySpan<byte> data)
    {
        if (data is not [ModIdDataDiscriminator, ..] || _ids is null)
            return true;

        var ids = ReadIds(data);
        return ids.Count == _ids.Count && ids.All(id => _ids.TryGetValue(id.Key, out var firstId) && firstId == id.Value);
    }

    // Acknowledgements name the phase they complete, which tells them apart.
    private static (byte Discriminator, byte Phase) ReadKey(ReadOnlySpan<byte> data)
    {
        return data switch
        {
            [AcknowledgementDiscriminator, var phase, ..] => (AcknowledgementDiscriminator, phase),
            [var discriminator, ..] => (discriminator, 0),
            _ => throw new InvalidDataException($"An empty {Channel} message has no discriminator.")
        };
    }

    private static Dictionary<string, int> ReadIds(ReadOnlySpan<byte> data)
    {
        var buffer = new MinecraftBuffer(data[1..]);
        var count = buffer.ReadVarInt();
        var ids = new Dictionary<string, int>(count);

        for (var index = 0; index < count; index++)
            ids[buffer.ReadString()] = buffer.ReadVarInt();

        return ids;
    }
}
