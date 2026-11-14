using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.NETCore.Client;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Tells what keeps a plugin that refuses to unload alive. The proxy can't read its own heap while it runs, so it writes a dump of itself and
/// has another process of its own executable read the dump. A runtime without createdump and DAC, as on Android, sends the proxy a snapshot of
/// its heap instead.
/// </summary>
internal static class PluginRetentionInspector
{
    /// <summary>The argument that starts the proxy's executable as the analyzer of a dump, followed by the dump, the handle and the DAC.</summary>
    public const string AnalyzeArgument = "--analyze-plugin-retention";

    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(2);

    // Yama lets a Linux process trace only its own descendants unless the traced one allows another, as the runtime does for its createdump
    private const int SetPtracer = 0x59616d61;
    private const nint AnyPtracer = -1;

    /// <summary>Describes the chains of references that keep the plugin alive.</summary>
    /// <param name="plugin">A weak handle to a type of the plugin or to its load context.</param>
    /// <returns>The chains as code reads them, and the same chains as the runtime names their frames.</returns>
    public static async ValueTask<(string Chains, string Frames)> DescribeAsync(GCHandle plugin, CancellationToken cancellationToken)
    {
        if (!CarriesRuntimeDiagnostics(CreateDumpFileName) || !CarriesRuntimeDiagnostics(DacFileName))
            return (await PluginRetentionSnapshot.DescribeAsync(plugin, cancellationToken), string.Empty);

        // The dump holds the proxy's memory, and a single-file build runs the createdump it carries from next to it, so both go to a directory
        // made for this one description, which only the proxy's user can read or write
        var directory = Directory.CreateTempSubdirectory("void-plugin-retention-").FullName;
        var dumpPath = Path.Combine(directory, $"{Environment.ProcessId}.dmp");

        try
        {
            await WriteDumpAsync(dumpPath, directory, cancellationToken);
            var output = await AnalyzeAsync(dumpPath, (ulong)GCHandle.ToIntPtr(plugin), directory, cancellationToken);

            return output.Split($"\n{PluginRetentionAnalyzer.FramesHeader}\n", 2) is [var chains, var frames] ? (chains.TrimEnd(), frames.TrimEnd()) : (output, string.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Tells whether the arguments start the proxy's executable as an analyzer.</summary>
    public static bool IsAnalyzer(string[] arguments)
    {
        return arguments is [AnalyzeArgument, ..];
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

    // The runtime writes the dump itself, with the createdump next to it, on every system. A single-file build has none there, so it runs the
    // one it carries on itself.
    private static async ValueTask WriteDumpAsync(string dumpPath, string directory, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), CreateDumpFileName)))
        {
            var client = new DiagnosticsClient(Environment.ProcessId);
            await client.WriteDumpAsync(DumpType.Full, dumpPath, logDumpGeneration: false, cancellationToken);
            return;
        }

        var startInfo = new ProcessStartInfo(ExtractRuntimeDiagnosticsFile(CreateDumpFileName, directory))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--full");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(dumpPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        // The pid of createdump is known only once it runs, when it may trace the proxy already, so any process may trace it meanwhile
        if (OperatingSystem.IsLinux())
            AllowPtracer(AnyPtracer);

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("createdump didn't start");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode is not 0 || !File.Exists(dumpPath))
                throw new InvalidOperationException($"createdump failed with exit code {process.ExitCode}: {await errorTask}{await outputTask}");
        }
        finally
        {
            if (OperatingSystem.IsLinux())
                AllowPtracer(0);
        }
    }

    // Fails where Yama isn't there to restrict tracing, which leaves nothing to allow. glibc, musl and bionic name their C library apart, which
    // the proxy has loaded whichever it is, so prctl is found among the symbols the proxy has loaded.
    private static void AllowPtracer(nint ptracer)
    {
        if (!NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "prctl", out var prctl))
            return;

        Marshal.GetDelegateForFunctionPointer<SetProcessOption>(prctl)(SetPtracer, ptracer, 0, 0, 0);
    }

    private delegate int SetProcessOption(int option, nint argument2, nint argument3, nint argument4, nint argument5);

    private static ValueTask<string> AnalyzeAsync(string dumpPath, ulong handle, string directory, CancellationToken cancellationToken)
    {
        var dacPath = File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), DacFileName)) ? Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), DacFileName) : ExtractRuntimeDiagnosticsFile(DacFileName, directory);
        return RunAnalyzerAsync([AnalyzeArgument, dumpPath, handle.ToString(), dacPath], cancellationToken);
    }

    private static async ValueTask<string> RunAnalyzerAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = CreateAnalyzerStartInfo();

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

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
        catch (OperationCanceledException exception)
        {
            process.Kill(entireProcessTree: true);

            if (cancellationToken.IsCancellationRequested)
                throw;

            // The analyzer ran out of the time it has, which nobody cancelled
            throw new TimeoutException($"The analyzer took longer than {AnalysisTimeout.TotalMinutes:0} minutes", exception);
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

        if (Environment.ProcessPath is { } processPath && Path.GetFileName(processPath) == hostName)
            return processPath;

        // The runtime lives in shared/Microsoft.NETCore.App/<version> of the installation the host is in.
        return Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", hostName));
    }

    private static bool CarriesRuntimeDiagnostics(string fileName)
    {
        return File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), fileName)) || typeof(PluginRetentionInspector).Assembly.GetManifestResourceInfo($"RuntimeDiagnostics/{fileName}") is not null;
    }

    private static string DacFileName => OperatingSystem.IsWindows() ? "mscordaccore.dll" : OperatingSystem.IsMacOS() ? "libmscordaccore.dylib" : "libmscordaccore.so";

    private static string CreateDumpFileName => OperatingSystem.IsWindows() ? "createdump.exe" : "createdump";

    // A single-file build carries the createdump and DAC of its runtime as resources, which it writes to a directory of its own to run them.
    private static string ExtractRuntimeDiagnosticsFile(string fileName, string directory)
    {
        using var resource = typeof(PluginRetentionInspector).Assembly.GetManifestResourceStream($"RuntimeDiagnostics/{fileName}")
            ?? throw new FileNotFoundException($"The proxy carries no {fileName} of its runtime", fileName);

        var path = Path.Combine(directory, fileName);

        using (var file = File.Create(path))
            resource.CopyTo(file);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }
}
