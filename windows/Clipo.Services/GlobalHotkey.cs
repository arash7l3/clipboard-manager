using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>A shortcut: modifier keys and one virtual key code (<c>'A'</c> to <c>'Z'</c> and <c>'0'</c> to <c>'9'</c> are their characters).</summary>
public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Ctrl+Shift+V opens the history (decision 0004).</summary>
    public static HotkeyChord ShowHistory { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'V');

    /// <summary>The shortcut as people write it, such as <c>Ctrl+Shift+V</c>.</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }
            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }
            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }
            if (Modifiers.HasFlag(HotkeyModifiers.Windows))
            {
                parts.Add("Win");
            }
            parts.Add(VirtualKey is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? ((char)VirtualKey).ToString() : $"0x{VirtualKey:X2}");
            return string.Join('+', parts);
        }
    }
}

/// <summary>
/// One system-wide shortcut, registered with <c>RegisterHotKey</c>. Unlike a
/// keyboard hook it needs no special permission, and the key press is not
/// passed on to the focused app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkey : IDisposable
{
    private const int Id = 1;
    private const uint NoRepeat = 0x4000;

    private readonly MessageWindow _window;
    private bool _registered;

    public GlobalHotkey(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    /// <summary>Raised on the UI thread each time the shortcut is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>The registered shortcut, if any.</summary>
    public HotkeyChord? Chord { get; private set; }

    /// <summary>
    /// Registers <paramref name="chord"/>, replacing the one registered
    /// before. Returns whether it worked; it fails when another app holds the
    /// same shortcut, and then no shortcut is registered.
    /// </summary>
    public bool Register(HotkeyChord chord)
    {
        Unregister();
        if (!NativeMethods.RegisterHotKey(_window.Handle, Id, (uint)chord.Modifiers | NoRepeat, chord.VirtualKey))
        {
            return false;
        }
        _registered = true;
        Chord = chord;
        return true;
    }

    public void Unregister()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, Id);
            _registered = false;
        }
        Chord = null;
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        Unregister();
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == NativeMethods.WmHotkey && e.WParam == Id)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }
}
