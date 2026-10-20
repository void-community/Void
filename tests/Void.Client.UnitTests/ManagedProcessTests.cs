using System.Diagnostics;
using Void.Client;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class ManagedProcessTests
{
    [Fact]
    public async Task ExitIsReportedWhileAnotherProcessStillHoldsTheOutput()
    {
        // Issue #1611: a process that inherited the game's output kept it open, so the exit was never reported and stop hung.
        using var process = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false }) ?? throw new InvalidOperationException("true did not start");
        TaskCompletionSource heldOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var managedProcess = new ManagedProcess(process, memoryMb: null, initialOutOfMemoryKillCount: null, heldOutput.Task, TimeSpan.FromMilliseconds(200));

        await managedProcess.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(managedProcess.HasExited);
        Assert.False(heldOutput.Task.IsCompleted);
    }

    [Fact]
    public async Task OutputThatEndsInTimeIsAwaited()
    {
        using var process = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false }) ?? throw new InvalidOperationException("true did not start");
        TaskCompletionSource output = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var managedProcess = new ManagedProcess(process, memoryMb: null, initialOutOfMemoryKillCount: null, output.Task, TimeSpan.FromSeconds(30));

        var exit = managedProcess.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.False(exit.IsCompleted);

        output.SetResult();
        await exit.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void OutOfMemoryNeedsTheCgroupCounterToRise()
    {
        // Without a counter captured at launch nothing can be attributed to the kernel's OOM killer, whatever the exit code.
        using var process = Process.Start(new ProcessStartInfo("true") { UseShellExecute = false }) ?? throw new InvalidOperationException("true did not start");
        process.WaitForExit();
        using var managedProcess = new ManagedProcess(process, memoryMb: 2048, initialOutOfMemoryKillCount: null);

        Assert.False(managedProcess.WasOutOfMemoryKilled);
    }
}
