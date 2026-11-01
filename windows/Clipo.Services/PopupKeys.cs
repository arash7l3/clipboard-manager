using System.Globalization;

namespace Clipo.Services;

/// <summary>What a key press asks the open popup to do.</summary>
public enum PopupKeyAction
{
    /// <summary>Nothing; the key is kept from the app in front all the same.</summary>
    None,
    Up,
    Down,
    PageUp,
    PageDown,
    Home,
    End,
    NextTab,
    PreviousTab,

    /// <summary>Picks the selected row.</summary>
    Pick,

    /// <summary>Picks row <see cref="PopupKeyCommand.Number"/> (1 to 9).</summary>
    QuickPaste,

    /// <summary>Pins or unpins the selected row.</summary>
    Pin,
    Close,

    /// <summary>Adds <see cref="PopupKeyCommand.Text"/> to the search.</summary>
    Type,

    /// <summary>Deletes the last character of the search.</summary>
    Backspace,

    /// <summary>Deletes the last word of the search.</summary>
    DeleteWord,
}

/// <summary>A key press for the popup.</summary>
/// <param name="Action">What to do.</param>
/// <param name="Plain">For a pick: paste without formatting (Shift was held).</param>
/// <param name="Number">For a quick paste: the row, 1 to 9.</param>
/// <param name="Text">For typing: the characters.</param>
public readonly record struct PopupKeyCommand(PopupKeyAction Action, bool Plain = false, int Number = 0, string Text = "");

/// <summary>
/// The popup's keys. The popup never takes the focus from the app the user
/// is typing in, so its keys are caught on their way there
/// (<see cref="PopupInput"/>) and turned into commands here. Keys the popup
/// has no use for, such as Alt+Tab, the Windows key, F-keys or media keys,
/// go on to the app or to Windows.
/// </summary>
public static class PopupKeys
{
    private const uint Back = 0x08;
    private const uint Tab = 0x09;
    private const uint Enter = 0x0D;
    private const uint Escape = 0x1B;
    private const uint Space = 0x20;
    private const uint PageUp = 0x21;
    private const uint PageDown = 0x22;
    private const uint End = 0x23;
    private const uint Home = 0x24;
    private const uint Left = 0x25;
    private const uint Up = 0x26;
    private const uint Right = 0x27;
    private const uint Down = 0x28;
    private const uint Insert = 0x2D;
    private const uint Delete = 0x2E;
    private const uint NumPad0 = 0x60;
    private const uint F4 = 0x73;

    /// <summary>A character sent as such, by the touch keyboard or a typing tool, rather than as a key.</summary>
    public const uint Packet = 0xE7;

    /// <summary>
    /// The command for a key press, or <c>null</c> to let the key through to
    /// the app in front.
    /// </summary>
    /// <param name="virtualKey">The key's virtual-key code.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="text">What the key types with those modifiers under the keyboard layout of the app in front; empty if nothing.</param>
    /// <param name="hotkey">The shortcut that opens the popup; pressed again, it closes it.</param>
    public static PopupKeyCommand? Classify(uint virtualKey, HotkeyModifiers modifiers, string text, HotkeyChord? hotkey)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsModifier(virtualKey))
        {
            return null;
        }
        if (hotkey is { } chord && chord.VirtualKey == virtualKey && chord.Modifiers == modifiers)
        {
            return new(PopupKeyAction.Close);
        }
        var control = modifiers.HasFlag(HotkeyModifiers.Control);
        var alt = modifiers.HasFlag(HotkeyModifiers.Alt);
        var shift = modifiers.HasFlag(HotkeyModifiers.Shift);
        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            return null;
        }
        if (control && alt)
        {
            // AltGr types characters on many layouts.
            return text.Length > 0 && IsTypingKey(virtualKey) ? new(PopupKeyAction.Type, Text: text) : null;
        }
        if (alt)
        {
            // Alt+F4 closed the popup when it had the focus; it must not close the app in front now.
            return virtualKey == F4 ? new(PopupKeyAction.Close) : null;
        }
        if (control)
        {
            return ControlKey(virtualKey, shift);
        }
        if (Navigation(virtualKey, shift) is { } command)
        {
            return command;
        }
        if (IsTypingKey(virtualKey))
        {
            // A dead key types nothing yet; it is kept from the app all the same.
            return text.Length > 0 ? new(PopupKeyAction.Type, Text: text) : new(PopupKeyAction.None);
        }
        return null;
    }

    /// <summary>1 to 9 for the digit keys, in the top row or on the number pad.</summary>
    public static int? QuickPasteNumber(uint virtualKey) => virtualKey switch
    {
        >= '1' and <= '9' => (int)(virtualKey - '0'),
        >= NumPad0 + 1 and <= NumPad0 + 9 => (int)(virtualKey - NumPad0),
        _ => null,
    };

    /// <summary><paramref name="search"/> without its last character, which may be a surrogate pair or a combined character.</summary>
    public static string Backspace(string search)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Length == 0)
        {
            return search;
        }
        var starts = StringInfo.ParseCombiningCharacters(search);
        return search[..starts[^1]];
    }

    /// <summary><paramref name="search"/> without its last word and the spaces after it, as Ctrl+Backspace does in a text field.</summary>
    public static string DeleteWord(string search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var end = search.Length;
        while (end > 0 && char.IsWhiteSpace(search[end - 1]))
        {
            end--;
        }
        while (end > 0 && !char.IsWhiteSpace(search[end - 1]))
        {
            end--;
        }
        return search[..end];
    }

    private static PopupKeyCommand? ControlKey(uint virtualKey, bool shift)
    {
        if (virtualKey == Escape)
        {
            // Ctrl+Esc opens Start; Ctrl+Shift+Esc, the Task Manager.
            return null;
        }
        if (virtualKey == 'P')
        {
            return new(PopupKeyAction.Pin);
        }
        if (QuickPasteNumber(virtualKey) is { } number)
        {
            return new(PopupKeyAction.QuickPaste, Plain: shift, Number: number);
        }
        if (virtualKey == Back)
        {
            return new(PopupKeyAction.DeleteWord);
        }
        if (Navigation(virtualKey, shift) is { } command)
        {
            return command;
        }
        // Ctrl+A, Ctrl+C and the like would act on the field the user is in.
        return IsTypingKey(virtualKey) ? new(PopupKeyAction.None) : null;
    }

    private static PopupKeyCommand? Navigation(uint virtualKey, bool shift) => virtualKey switch
    {
        Up => new(PopupKeyAction.Up),
        Down => new(PopupKeyAction.Down),
        PageUp => new(PopupKeyAction.PageUp),
        PageDown => new(PopupKeyAction.PageDown),
        Home => new(PopupKeyAction.Home),
        End => new(PopupKeyAction.End),
        Tab => new(shift ? PopupKeyAction.PreviousTab : PopupKeyAction.NextTab),
        Enter => new(PopupKeyAction.Pick, Plain: shift),
        Escape => new(PopupKeyAction.Close),
        Back => new(PopupKeyAction.Backspace),
        // The search has no caret to move; these would edit the field underneath.
        Left or Right or Delete or Insert => new(PopupKeyAction.None),
        _ => null,
    };

    /// <summary>Shift, Ctrl, Alt, the Windows keys and the lock keys: always passed on, so Windows keeps track of them.</summary>
    public static bool IsModifier(uint virtualKey) => virtualKey is
        0x10 or 0x11 or 0x12 // Shift, Ctrl, Alt
        or (>= 0xA0 and <= 0xA5) // left and right Shift, Ctrl, Alt
        or 0x5B or 0x5C // the Windows keys
        or 0x14 or 0x90 or 0x91; // Caps Lock, Num Lock, Scroll Lock

    /// <summary>The keys that type: space, digits, letters, the number pad, the punctuation keys and characters sent as such.</summary>
    private static bool IsTypingKey(uint virtualKey) => virtualKey is
        Space
        or Packet
        or (>= '0' and <= '9')
        or (>= 'A' and <= 'Z')
        or (>= NumPad0 and <= 0x6F)
        or (>= 0xBA and <= 0xC0)
        or (>= 0xDB and <= 0xDF)
        or 0xE2;
}
