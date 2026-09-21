namespace Void.Client.States;

/// <summary>Outcome of the most recently accepted operation.</summary>
internal enum OperationState
{
    None,
    Running,
    Succeeded,
    Failed,
    Canceled
}
