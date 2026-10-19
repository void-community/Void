using Void.Proxy.Api.Links;

namespace Void.Proxy.Plugins.ModsSupport.SlowJoin;

/// <summary>
/// The grace period of one join. The proxy answers the backend's keep-alives while the player loads, and probes the player with
/// keep-alives of its own. A probe reaches the player after everything the backend sent before it, but mods such as JEI do their
/// heavy loading on later client ticks and answer between those steps, so one prompt answer doesn't show that the player has
/// finished loading. The grace period ends once the player has answered several probes in a row promptly.
/// </summary>
/// <param name="link">The link the player joined through.</param>
/// <param name="timeout">How long the player may take to answer a probe.</param>
/// <param name="promptAnswers">How many probes in a row the player must answer promptly.</param>
/// <param name="promptAnswerTime">How soon an answer must arrive to count as prompt.</param>
public class SlowJoinGrace(ILink link, TimeSpan timeout, int promptAnswers, TimeSpan promptAnswerTime)
{
    private readonly Lock _lock = new();
    private long? _probeId;
    private DateTimeOffset _probeSentAt;
    private CancellationTokenSource? _probeSettled;
    private int _answeredPromptly;
    private bool _over;

    public ILink Link { get; } = link;

    public TimeSpan Timeout { get; } = timeout;

    public int PromptAnswers { get; } = promptAnswers;

    public TimeSpan PromptAnswerTime { get; } = promptAnswerTime;

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
    /// <param name="probeId">The keep-alive id of the probe.</param>
    /// <param name="now">When the probe is sent.</param>
    /// <param name="settled">
    /// Cancelled once the probe is answered, times out or the grace period ends, so a wait for its timeout never outlives the probe.
    /// </param>
    /// <returns><see langword="true" /> when the probe should be sent; otherwise, <see langword="false" />.</returns>
    public bool TryStartProbe(long probeId, DateTimeOffset now, out CancellationToken settled)
    {
        lock (_lock)
        {
            settled = CancellationToken.None;

            if (_over || _probeId is not null)
                return false;

            _probeId = probeId;
            _probeSentAt = now;
            _probeSettled = new CancellationTokenSource();
            settled = _probeSettled.Token;
            return true;
        }
    }

    /// <summary>
    /// Records the player's answer to the probe. A prompt answer extends the run of prompt answers, which ends the grace period once
    /// it is long enough, and a slow answer starts the run over.
    /// </summary>
    /// <param name="answeredId">The keep-alive the player answered.</param>
    /// <param name="now">When the answer arrived.</param>
    /// <param name="waited">How long the probe waited for the answer.</param>
    /// <param name="answeredPromptly">How many probes in a row the player has answered promptly, including this one.</param>
    /// <returns><see langword="true" /> when the player answered the probe; otherwise, <see langword="false" />.</returns>
    public bool TryAnswer(long answeredId, DateTimeOffset now, out TimeSpan waited, out int answeredPromptly)
    {
        lock (_lock)
        {
            waited = now - _probeSentAt;
            answeredPromptly = _answeredPromptly;

            if (_over || _probeId != answeredId)
                return false;

            _probeId = null;
            SettleProbe();
            _answeredPromptly = answeredPromptly = waited <= PromptAnswerTime ? _answeredPromptly + 1 : 0;
            _over = answeredPromptly >= PromptAnswers;
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
            SettleProbe();
            return true;
        }
    }

    public void End()
    {
        lock (_lock)
        {
            _over = true;
            SettleProbe();
        }
    }

    private void SettleProbe()
    {
        _probeSettled?.Cancel();
        _probeSettled?.Dispose();
        _probeSettled = null;
    }
}
