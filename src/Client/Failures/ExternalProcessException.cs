namespace Void.Client.Failures;

internal sealed class ExternalProcessException : Exception
{
    public ExternalProcessException()
    {
    }

    public ExternalProcessException(string fileName, IReadOnlyList<string> arguments, int exitCode, string standardOutput, string standardError)
        : base($"{fileName} exited with code {exitCode}: {standardError}")
    {
        FileName = fileName;
        Arguments = arguments;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public ExternalProcessException(string message) : base(message)
    {
    }

    public ExternalProcessException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public IReadOnlyList<string> Arguments { get; } = [];

    public int ExitCode { get; }

    public string FileName { get; } = string.Empty;

    public string StandardError { get; } = string.Empty;

    public string StandardOutput { get; } = string.Empty;
}
