namespace Void.Client;

internal interface IManagedProcess : IDisposable
{
    int? ExitCode { get; }
    bool HasExited { get; }
    int Identifier { get; }
    int? MemoryMb { get; }
    bool WasOutOfMemoryKilled { get; }
    void KillTree();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}
