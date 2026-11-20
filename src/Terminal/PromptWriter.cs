using System.Text;

namespace Void.Terminal;

public class PromptWriter(PromptReader reader, StreamWriter writer) : TextWriter, IDisposable
{
    static PromptWriter()
    {
        if (WindowsConsole.TryEnableVirtualTerminalProcessing())
            return;

        throw new NotSupportedException("Virtual terminal processing is not supported.");
    }

    public override Encoding Encoding => writer.Encoding;

    public override void Write(string? value)
    {
        WriteSpan(value);
    }

    public override void Write(ReadOnlySpan<char> value)
    {
        WriteSpan(value);
    }

    public override void WriteLine()
    {
        WriteSpanLine();
    }

    public override void WriteLine(string? value)
    {
        if (value is null)
            return;

        WriteSpanLine(value);
    }

    public void WriteSpanLine()
    {
        writer.WriteLine();
        WriteBufferLines();
    }

    public void WriteSpanLine(ReadOnlySpan<char> value)
    {
        WriteSpan(value);
        WriteSpanLine();
    }

    public void WriteSpan(ReadOnlySpan<char> value)
    {
        if (value.Length is 0)
            return;

        writer.Write(value);
        ClearCursorLine();
    }

    public void UpdateBuffer(int lengthBefore = 0)
    {
        if (lengthBefore > reader.Buffer.Length + reader.Prompt.Length)
            ClearCursorLine();
        else
            SetCursorLeft();

        WriteBufferLines(lengthBefore);
    }

    private void WriteBufferLines(int lengthBefore = 0)
    {
        if (!reader.IsActive)
            return;

        writer.Write(reader.Prompt);
        writer.Write(reader.Buffer);

        SetCursorLeft();
        var lines = GetBufferLines(lengthBefore);
        for (var i = 0; i < lines - 1; i++)
            SetCursorUp();
    }

    private int GetBufferLines(int length = 0)
    {
        // This width is not safe when the console is resized.
        var width = PromptReader.Width;

        // A terminal that tells no width, as a pseudo-terminal nobody sized does, has no line to wrap the buffer at
        if (width <= 0)
            return 1;

        if (length is 0)
            length = reader.Buffer.Length + reader.Prompt.Length;

        if (width == length)
        {
            if (OperatingSystem.IsWindows() && Console.CursorLeft is 0)
                return 2;

            return 1;
        }

        var lines = Math.Max(1, (length + width - 1) / width);
        return lines;
    }

    public void HideCursor()
    {
        WriteAnsiCommand('l', "?25");
    }

    public void ShowCursor()
    {
        WriteAnsiCommand('h', "?25");
    }

    public void ResetStyle()
    {
        WriteAnsiCommand('m', '0');
    }

    private void SetCursorUp(char value = '0')
    {
        WriteAnsiCommand('A', value);
    }

    private void SetCursorLeft(char value = '0')
    {
        WriteAnsiCommand('G', value);
    }

    private void ClearCursorLine(char value = '0')
    {
        WriteAnsiCommand('J', value);
    }

    private void WriteAnsiCommand(char command, params ReadOnlySpan<char> parameters)
    {
        writer.Write('\x1B');
        writer.Write('[');

        foreach (var parameter in parameters)
            writer.Write(parameter);

        writer.Write(command);
    }
}
