using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.NETCore.Client;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Tells what keeps a plugin that refuses to unload alive. The proxy can't read its own heap while it runs, so it writes a dump of itself and
/// has another process of its own executable read the dump.
/// </summary>
internal static class PluginRetentionInspector
{
    /// <summary>The argument that starts the proxy's executable as the analyzer of a dump, followed by the dump, the handle and the DAC.</summary>
    public const string AnalyzeArgument = "--analyze-plugin-retention";

    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Describes the chains of references that keep the plugin alive, one per line.</summary>
    /// <param name="pluginType">A weak handle to a type of the plugin.</param>
    public static async ValueTask<string> DescribeAsync(GCHandle pluginType, CancellationToken cancellationToken)
    {
        var dumpPath = Path.Combine(Path.GetTempPath(), $"void-plugin-retention-{Environment.ProcessId}-{Guid.NewGuid():N}.dmp");

        try
        {
            await WriteDumpAsync(dumpPath, cancellationToken);
            return await AnalyzeAsync(dumpPath, (ulong)GCHandle.ToIntPtr(pluginType), cancellationToken);
        }
        finally
        {
            File.Delete(dumpPath);
        }
    }

    /// <summary>Runs the analyzer that <see cref="AnalyzeArgument" /> starts.</summary>
    /// <returns>The exit code of the analyzer.</returns>
    public static int Analyze(string[] arguments)
    {
        if (arguments is not [AnalyzeArgument, var dumpPath, var handleText, .. var rest] || !ulong.TryParse(handleText, out var handle))
        {
            System.Console.Error.WriteLine($"Usage: {AnalyzeArgument} <dump> <handle> [dac]");
            return 1;
        }

        PluginRetentionAnalyzer.Analyze(dumpPath, handle, rest is [var dacPath, ..] ? dacPath : null, System.Console.Out);
        return 0;
    }

    // The runtime writes the dump itself, which works whether or not the system lets one process read another.
    private static async ValueTask WriteDumpAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var client = new DiagnosticsClient(Environment.ProcessId);
        await client.WriteDumpAsync(DumpType.Full, dumpPath, logDumpGeneration: false, cancellationToken);
    }

    private static async ValueTask<string> AnalyzeAsync(string dumpPath, ulong handle, CancellationToken cancellationToken)
    {
        var startInfo = CreateAnalyzerStartInfo();
        startInfo.ArgumentList.Add(AnalyzeArgument);
        startInfo.ArgumentList.Add(dumpPath);
        startInfo.ArgumentList.Add(handle.ToString());
        startInfo.ArgumentList.Add(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), DacFileName));
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AnalysisTimeout);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The analyzer didn't start");

        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await outputTask;

        if (process.ExitCode is not 0)
            throw new InvalidOperationException($"The analyzer failed with exit code {process.ExitCode}: {await errorTask}{output}");

        return output.TrimEnd();
    }

    // The proxy runs as its own executable when it's published as one, and as an assembly of the dotnet host otherwise, as in tests.
    private static ProcessStartInfo CreateAnalyzerStartInfo()
    {
        var assemblyPath = typeof(PluginRetentionInspector).Assembly.Location;

        if (string.IsNullOrEmpty(assemblyPath))
            return new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The proxy's executable is unknown"));

        var startInfo = new ProcessStartInfo(FindDotNetHost());
        startInfo.ArgumentList.Add(assemblyPath);

        return startInfo;
    }

    private static string FindDotNetHost()
    {
        var hostName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath)
            return hostPath;

        if (Path.GetFileName(Environment.ProcessPath) == hostName)
            return Environment.ProcessPath!;

        // The runtime lives in shared/Microsoft.NETCore.App/<version> of the installation the host is in.
        return Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", hostName));
    }

    private static string DacFileName => OperatingSystem.IsWindows() ? "mscordaccore.dll" : OperatingSystem.IsMacOS() ? "libmscordaccore.dylib" : "libmscordaccore.so";
}
