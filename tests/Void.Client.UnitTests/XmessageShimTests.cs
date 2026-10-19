using System.Diagnostics;
using Xunit;

namespace Void.Client.UnitTests;

public sealed class XmessageShimTests
{
    [Theory]
    [InlineData("Ok:1,Cancel:0", 0, "Cancel")]
    [InlineData("Yes:1,No:0", 0, "No")]
    [InlineData("Yes:1,No:2,Cancel:0", 0, "Cancel")]
    [InlineData("Ok:1", 1, "Ok")]
    [InlineData("Retry,Abort", 102, "Abort")]
    public async Task AnswersDialogsWithTheirNegativeButton(string buttons, int expectedExitCode, string expectedLabel)
    {
        // The same command line LWJGL's TinyFileDialogs runs for tinyfd_messageBox.
        var result = await RunAsync("-buttons", buttons, "-default", "Ok", "-center", "Low memory.\nClick OK to quit.", "-title", "Memory Settings");

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Equal($"Void client warning: Dialog \"Memory Settings\" answered with {expectedLabel}: Low memory. Click OK to quit.\n", result.StandardError);
    }

    [Fact]
    public async Task AnswersAPlainMessageWithItsOnlyButton()
    {
        var result = await RunAsync("-print", "Hello", "world");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("okay\n", result.StandardOutput);
        Assert.Equal("Void client warning: Dialog answered with okay: Hello world\n", result.StandardError);
    }

    private static async Task<ShimResult> RunAsync(params string[] arguments)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The xmessage shim is a bash script for the Linux client image");

        ProcessStartInfo startInformation = new(fileName: "bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInformation.ArgumentList.Add(FindShim());

        foreach (string argument in arguments)
            startInformation.ArgumentList.Add(argument);

        using var process = Process.Start(startInformation) ?? throw new InvalidOperationException("bash could not be started");
        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new(process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindShim()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Client", "xmessage");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/Client/xmessage was not found above the test output directory");
    }

    private sealed record ShimResult(int ExitCode, string StandardOutput, string StandardError);
}
