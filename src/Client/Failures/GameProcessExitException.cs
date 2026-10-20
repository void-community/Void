namespace Void.Client.Failures;

internal sealed class GameProcessExitException : GameClientException
{
    public GameProcessExitException()
    {
    }

    // A crash report proves the game ended on its own, while the out-of-memory count covers every process in the container.
    public GameProcessExitException(bool wasOutOfMemoryKilled, int? memoryMb, string? crashCause) : base(
        crashCause is not null ? "client.process.crashed" : wasOutOfMemoryKilled ? "client.process.out_of_memory" : "client.process.exited",
        operation: "process",
        crashCause is not null ? "crash-report" : "exit",
        crashCause ?? CreateMessage(wasOutOfMemoryKilled, memoryMb)
    )
    {
    }

    public GameProcessExitException(string message) : base(message)
    {
    }

    public GameProcessExitException(string message, Exception innerException) : base(message, innerException)
    {
    }

    // The exit code belongs to the launcher, which exits 0 whatever the game did, so it can't describe how Minecraft ended.
    private static string CreateMessage(bool wasOutOfMemoryKilled, int? memoryMb)
    {
        if (!wasOutOfMemoryKilled)
            return "Minecraft exited without a stop request";

        string memoryDescription = memoryMb is { } value ? $" with a configured maximum heap of {value} MiB" : "";

        return $"Minecraft was killed by the operating system out-of-memory killer{memoryDescription}";
    }
}
