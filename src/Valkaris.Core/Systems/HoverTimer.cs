namespace Valkaris.Core.Systems;

/// <summary>
/// When a hover card shows: after the cursor has rested on something for a moment, and for as long as it stays there or
/// on the card, with a short grace period to cross the gap between them.
/// </summary>
public sealed class HoverTimer<T> where T : struct
{
    public const double OpenDelaySeconds = 0.3;
    public const double GraceSeconds = 0.3;

    private T? _candidate;
    private double _rested;
    private double _away;

    /// <summary>What the card is showing, if anything.</summary>
    public T? Shown { get; private set; }

    public void Update(T? under, bool overCard, double delta)
    {
        if (overCard && Shown is not null)
        {
            _away = 0;
            _candidate = null;
            return;
        }

        if (under is null)
        {
            _candidate = null;
            _away += delta;
            if (_away >= GraceSeconds)
                Shown = null;
            return;
        }

        _away = 0;
        if (Equals(under, Shown))
        {
            _candidate = null;
            return;
        }

        if (!Equals(under, _candidate))
        {
            _candidate = under;
            _rested = 0;
        }

        _rested += delta;
        if (_rested >= OpenDelaySeconds)
        {
            Shown = under;
            _candidate = null;
        }
    }

    public void Clear()
    {
        Shown = null;
        _candidate = null;
        _rested = 0;
        _away = 0;
    }
}
