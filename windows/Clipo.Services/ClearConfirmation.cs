namespace Clipo.Services;

/// <summary>
/// The popup's Clear button asks twice: the first press arms it, and only a
/// second press within <paramref name="window"/> clears. Inline, because a
/// dialog would take the focus and close the popup.
/// </summary>
/// <param name="window">How long an armed button waits for the second press.</param>
/// <param name="clock">The current time.</param>
public sealed class ClearConfirmation(TimeSpan window, Func<DateTime> clock)
{
    /// <summary>The usual wait for the second press.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(3);

    private DateTime? _armedAt;

    public ClearConfirmation()
        : this(DefaultWindow, () => DateTime.UtcNow)
    {
    }

    /// <summary>How long an armed button waits for the second press.</summary>
    public TimeSpan Window => window;

    /// <summary>The button is waiting for the second press.</summary>
    public bool IsArmed => _armedAt is { } armed && clock() - armed < window;

    /// <summary>A press. Returns <c>true</c> when it is the confirming one and the tab should be cleared.</summary>
    public bool Press()
    {
        if (IsArmed)
        {
            _armedAt = null;
            return true;
        }
        _armedAt = clock();
        return false;
    }

    /// <summary>Back to the first press, as after switching tabs or closing the popup.</summary>
    public void Disarm() => _armedAt = null;
}
