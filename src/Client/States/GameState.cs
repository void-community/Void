namespace Void.Client;

/// <summary>Stable lifecycle states exposed by the client API.</summary>
internal enum GameState
{
    Idle,
    Starting,
    Ready,
    Connected,
    Stopping,
    Failed
}
