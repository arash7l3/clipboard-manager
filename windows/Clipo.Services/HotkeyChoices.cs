namespace Clipo.Services;

/// <summary>The shortcuts <see cref="HotkeyChoice"/> stands for.</summary>
public static class HotkeyChoices
{
    /// <summary>The choice's modifier keys with the V key.</summary>
    public static HotkeyChord Chord(HotkeyChoice choice) => new(Modifiers(choice), 'V');

    private static HotkeyModifiers Modifiers(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.ControlAlt => HotkeyModifiers.Control | HotkeyModifiers.Alt,
        HotkeyChoice.AltShift => HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        HotkeyChoice.ControlAltShift => HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        _ => HotkeyModifiers.Control | HotkeyModifiers.Shift,
    };
}
