namespace Void.Client;

internal sealed class GameProcessExitException : GameClientException
{
    public GameProcessExitException()
    {
    }

    public GameProcessExitException(int exitCode, bool wasOutOfMemoryKilled, int? memoryMb) : base(
        wasOutOfMemoryKilled ? "client.process.out_of_memory" : "client.process.exited",
        operation: "process",
        stage: "exit",
        CreateMessage(exitCode, wasOutOfMemoryKilled, memoryMb)
    )
    {
    }

    public GameProcessExitException(string message) : base(message)
    {
    }

    public GameProcessExitException(string message, Exception innerException) : base(message, innerException)
    {
    }

    private static string CreateMessage(int exitCode, bool wasOutOfMemoryKilled, int? memoryMb)
    {
        if (!wasOutOfMemoryKilled)
            return $"Minecraft exited unexpectedly with exit code {exitCode}";

        var memoryDescription = memoryMb is { } value ? $" with a configured maximum heap of {value} MiB" : "";

        return $"Minecraft was killed by the operating system out-of-memory killer with exit code {exitCode}{memoryDescription}";
    }
}
