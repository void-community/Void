using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Void.IntegrationTests.Infrastructure.Harness;

/// <summary>
/// Follows how little memory the runner has left while a collection plays, which a runner that runs out of it doesn't live to tell, as it
/// loses its job with its logs.
/// </summary>
internal sealed class MemorySampler : IAsyncDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private long _lowestAvailableKilobytes = long.MaxValue;
    private long _highestSwappedKilobytes;

    public MemorySampler()
    {
        _sampling = OperatingSystem.IsLinux() ? SampleAsync() : Task.CompletedTask;
    }

    /// <summary>Tells the least memory the runner had available and the most it had swapped out since the sampler started.</summary>
    public string Describe()
    {
        return _lowestAvailableKilobytes is long.MaxValue
            ? "no memory samples"
            : $"at least {_lowestAvailableKilobytes / 1024} MB of memory available and at most {_highestSwappedKilobytes / 1024} MB swapped out";
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _sampling;
        _stop.Dispose();
    }

    private async Task SampleAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync("/proc/meminfo", _stop.Token);
                var available = Read(lines, "MemAvailable:");
                var swapped = Read(lines, "SwapTotal:") - Read(lines, "SwapFree:");

                _lowestAvailableKilobytes = Math.Min(_lowestAvailableKilobytes, available);
                _highestSwappedKilobytes = Math.Max(_highestSwappedKilobytes, swapped);

                await Task.Delay(SampleInterval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                return;
            }
        }
    }

    // Lines read like "MemAvailable:    1234567 kB"
    private static long Read(string[] lines, string name)
    {
        return lines.FirstOrDefault(line => line.StartsWith(name, StringComparison.Ordinal)) is { } line
            ? long.Parse(line[name.Length..].Trim().Split(' ')[0])
            : 0;
    }
}
