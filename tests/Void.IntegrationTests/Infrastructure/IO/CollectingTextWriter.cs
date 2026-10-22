using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Void.IntegrationTests.Infrastructure.IO;

public class CollectingTextWriter : TextWriter
{
    private readonly List<CollectedLine> _lines = [];
    private readonly StringBuilder _currentLineBuilder = new();
    private readonly Lock _lock = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            using var _ = _lock.EnterScope();
            return [.. _lines.Select(line => line.Text)];
        }
    }

    public string Text => string.Join('\n', Lines);
    public event Action<string>? OnLine;
    public override Encoding Encoding { get; } = Encoding.UTF8;

    public IReadOnlyList<string> GetLinesSince(DateTime since)
    {
        using var _ = _lock.EnterScope();
        return [.. _lines.Where(line => line.Timestamp >= since).Select(line => line.Text)];
    }

    public async Task<string> WaitForLineAsync(Func<string, bool> predicate, CancellationToken cancellationToken = default)
    {
        var completionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleLine(string line)
        {
            if (predicate(line))
                completionSource.TrySetResult(line);
        }

        OnLine += HandleLine;

        try
        {
            return await completionSource.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            OnLine -= HandleLine;
        }
    }

    public override void Write(char value)
    {
        string[] completedLines;

        using (var _ = _lock.EnterScope())
            completedLines = AppendAndCollectCompletedLines([value]);

        foreach (var completedLine in completedLines)
            OnLine?.Invoke(completedLine);
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;

        string[] completedLines;

        using (var _ = _lock.EnterScope())
            completedLines = AppendAndCollectCompletedLines(value);

        foreach (var completedLine in completedLines)
            OnLine?.Invoke(completedLine);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        string[] completedLines;

        using (var _ = _lock.EnterScope())
            completedLines = AppendAndCollectCompletedLines(buffer.AsSpan(index, count));

        foreach (var completedLine in completedLines)
            OnLine?.Invoke(completedLine);
    }

    private string[] AppendAndCollectCompletedLines(ReadOnlySpan<char> value)
    {
        List<string>? completedLines = null;

        foreach (var character in value)
        {
            if (character is '\n')
            {
                var completedLine = _currentLineBuilder.ToString().Trim();
                _currentLineBuilder.Clear();

                if (completedLine.Length == 0)
                    continue;

                _lines.Add(new CollectedLine(DateTime.UtcNow, completedLine));
                completedLines ??= [];
                completedLines.Add(completedLine);
                continue;
            }

            _currentLineBuilder.Append(character);
        }

        return completedLines?.ToArray() ?? [];
    }

    private readonly record struct CollectedLine(DateTime Timestamp, string Text);
}
