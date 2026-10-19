using Void.Proxy.Api.Links;

namespace Void.Proxy.Plugins.ModsSupport.SlowJoin;

/// <summary>
/// The grace period of one join. The proxy answers the backend's keep-alives while the player loads, and probes the player with a
/// keep-alive of its own. The probe reaches the player after everything the backend sent before it, so its answer shows that the
/// player has caught up and can answer the backend itself.
/// </summary>
/// <param name="link">The link the player joined through.</param>
/// <param name="timeout">How long the player may take to answer the probe.</param>
public class SlowJoinGrace(ILink link, TimeSpan timeout)
{
    private readonly Lock _lock = new();
    private long? _probeId;
    private DateTimeOffset _probeSentAt;
    private bool _over;

    public ILink Link { get; } = link;

    public TimeSpan Timeout { get; } = timeout;

    public bool IsOver
    {
        get
        {
            lock (_lock)
                return _over;
        }
    }

    /// <summary>
    /// Records a probe to send, unless one is already waiting for an answer or the grace period is over.
    /// </summary>
    /// <returns><see langword="true" /> when the probe should be sent; otherwise, <see langword="false" />.</returns>
    public bool TryStartProbe(long probeId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_over || _probeId is not null)
                return false;

            _probeId = probeId;
            _probeSentAt = now;
            return true;
        }
    }

    /// <summary>
    /// Ends the grace period when <paramref name="answeredId" /> answers the probe.
    /// </summary>
    /// <returns><see langword="true" /> when the player answered the probe; otherwise, <see langword="false" />.</returns>
    public bool TryEnd(long answeredId, DateTimeOffset now, out TimeSpan waited)
    {
        lock (_lock)
        {
            waited = now - _probeSentAt;

            if (_over || _probeId != answeredId)
                return false;

            _over = true;
            return true;
        }
    }

    /// <summary>
    /// Ends the grace period when the probe has waited for longer than <see cref="Timeout" />.
    /// </summary>
    /// <returns><see langword="true" /> when the player has timed out; otherwise, <see langword="false" />.</returns>
    public bool TryTimeOut(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_over || _probeId is null || now - _probeSentAt < Timeout)
                return false;

            _over = true;
            return true;
        }
    }

    public void End()
    {
        lock (_lock)
            _over = true;
    }
}
