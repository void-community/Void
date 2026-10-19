namespace Void.Client;

/// <summary>
/// Turns PortableMC's interactive output into plain lines for container logs and diagnostics. PortableMC starts every line with a
/// carriage return and redraws its status lines in place, which log viewers that render control characters hide entirely.
/// A carriage return at the start of a line is dropped, one that would redraw a line becomes a line break, and terminal escape
/// sequences are removed. Text may arrive in arbitrary pieces, so the state carries over between calls.
/// </summary>
internal sealed class ConsoleTextNormalizer
{
    private const char Escape = '\u001b';

    private bool _atLineStart = true;
    private EscapeState _escapeState;
    private bool _pendingCarriageReturn;

    private enum EscapeState
    {
        None,
        Escape,
        ControlSequence
    }

    /// <summary>
    /// Ends a line that a final carriage return would have redrawn.
    /// </summary>
    public string Flush()
    {
        bool endsLine = _pendingCarriageReturn && !_atLineStart;
        _pendingCarriageReturn = false;

        return endsLine ? "\n" : "";
    }

    public string Normalize(ReadOnlySpan<char> text)
    {
        // Every inserted line break replaces a carriage return, and only one can be left over from the previous call.
        char[] normalized = new char[text.Length + 1];
        int length = 0;

        foreach (char character in text)
        {
            if (SkipsEscapeSequence(character))
                continue;

            if (_pendingCarriageReturn)
            {
                _pendingCarriageReturn = false;

                if (character != '\n' && !_atLineStart)
                    length = Append(normalized, length, character: '\n');
            }

            if (character == '\r')
            {
                _pendingCarriageReturn = true;

                continue;
            }

            length = Append(normalized, length, character);
        }

        return new string(normalized, startIndex: 0, length);
    }

    private int Append(char[] normalized, int length, char character)
    {
        normalized[length] = character;
        _atLineStart = character == '\n';

        return length + 1;
    }

    private bool SkipsEscapeSequence(char character)
    {
        switch (_escapeState)
        {
            case EscapeState.Escape:
                {
                    _escapeState = character == '[' ? EscapeState.ControlSequence : EscapeState.None;

                    return true;
                }
            case EscapeState.ControlSequence:
                {
                    // Control sequences end with a final byte in the range @ to ~.
                    if (character is >= '@' and <= '~')
                        _escapeState = EscapeState.None;

                    return true;
                }
            default:
                {
                    if (character != Escape)
                        return false;

                    _escapeState = EscapeState.Escape;

                    return true;
                }
        }
    }
}
