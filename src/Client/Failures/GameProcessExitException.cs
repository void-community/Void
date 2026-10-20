namespace Void.Client.Failures;

internal sealed class GameProcessExitException : GameClientException
{
    public GameProcessExitException()
    {
    }

    public GameProcessExitException(bool wasOutOfMemoryKilled, int? memoryMb) : base(
        wasOutOfMemoryKilled ? "client.process.out_of_memory" : "client.process.exited",
        operation: "process",
        stage: "exit",
        CreateMessage(wasOutOfMemoryKilled, memoryMb)
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
