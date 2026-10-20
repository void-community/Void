namespace Void.Client.States;

/// <summary>Stable lifecycle states exposed by the client API.</summary>
internal enum GameState
{
    Idle,
    Starting,
    Loading,
    Ready,
    Connected,
    Stopping,
    Failed
}
