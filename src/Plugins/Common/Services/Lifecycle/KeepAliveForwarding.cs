using System.Runtime.CompilerServices;
using Void.Proxy.Api.Links;

namespace Void.Proxy.Plugins.Common.Services.Lifecycle;

/// <summary>
/// Remembers the keep-alive ids each backend sent to its player, so that the player's answers reach only the backend that asked.
/// </summary>
public class KeepAliveForwarding
{
    /// <summary>
    /// The most ids remembered per link. A backend has one keep-alive outstanding at a time, so this only bounds a player that
    /// never answers.
    /// </summary>
    public const int MaximumPendingRequests = 16;

    private readonly ConditionalWeakTable<ILink, List<long>> _pendingRequests = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Records a keep-alive request the backend sent through <paramref name="link" />.
    /// </summary>
    public void Remember(ILink link, long id)
    {
        lock (_lock)
        {
            var pendingRequests = _pendingRequests.GetOrCreateValue(link);

            if (pendingRequests.Count >= MaximumPendingRequests)
                pendingRequests.RemoveAt(0);

            pendingRequests.Add(id);
        }
    }

    /// <summary>
    /// Consumes a keep-alive request that the backend of <paramref name="link" /> is still waiting to have answered.
    /// </summary>
    /// <returns><see langword="true" /> when the answer belongs to that backend; otherwise, <see langword="false" />.</returns>
    public bool TryAccept(ILink link, long id)
    {
        lock (_lock)
            return _pendingRequests.TryGetValue(link, out var pendingRequests) && pendingRequests.Remove(id);
    }
}
