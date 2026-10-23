namespace Void.Client.Abstractions;

internal interface IManagedProcess : IDisposable
{
    string? CrashCause { get; }
    int? ExitCode { get; }
    bool HasExited { get; }
    int Id { get; }
    string? LastError { get; }
    int? MemoryMb { get; }
    bool WasOutOfMemoryKilled { get; }
    void KillTree();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}
