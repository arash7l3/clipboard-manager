using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>
/// The popup's keyboard and the clicks around it, while it is open. The
/// popup never takes the focus from the app the user is typing in, so its
/// keys are caught on their way there with a low-level keyboard hook and
/// kept from the app (<see cref="PopupKeys"/>); a mouse hook reports clicks
/// outside Clipo's windows, which close the popup. The hooks exist only
/// between <see cref="Start"/> and <see cref="Stop"/>, and run on a thread of
/// their own, so a busy UI thread cannot delay typing or make Windows drop
/// the hook. Its events are raised on that thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class PopupInput : IDisposable
{
    private const int KeyboardHook = 13;
    private const int MouseHook = 14;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmTimer = 0x0113;
    private const uint WmQuit = 0x0012;
    private const uint StartMessage = NativeMethods.WmApp + 1;
    private const uint StopMessage = NativeMethods.WmApp + 2;
    private const uint NoRemove = 0;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkCapital = 0x14;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkLeftControl = 0xA2;
    private const int VkRightMenu = 0xA5;
    private const uint KeepKeyboardState = 0x4;

    /// <summary>After the popup closes, keys pressed in it are kept from the app until let go, but no longer than this.</summary>
    private const uint LingerMilliseconds = 1500;

    /// <summary>The hook procedures are plain functions; they find the instance here. There is one popup.</summary>
    private static PopupInput? s_current;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private uint _threadId;

    // Used on the hook thread only.
    private nint _keyboardHook;
    private nint _mouseHook;
    private nuint _lingerTimer;
    private bool _active;
    private HotkeyChord? _hotkey;

    /// <summary>Keys the popup took; their repeats and their release are kept from the app too.</summary>
    private readonly HashSet<uint> _taken = [];

    /// <summary>Keys already held when the popup opened, such as the V of the shortcut; their repeats are dropped.</summary>
    private readonly HashSet<uint> _heldBefore = [];

    public PopupInput()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Clipo popup input" };
        _thread.Start();
        _ready.Wait();
        s_current = this;
    }

    /// <summary>A key for the popup was pressed. Raised on the hook thread.</summary>
    public event EventHandler<PopupKeyCommand>? KeyPressed;

    /// <summary>A mouse button went down outside Clipo's windows. Raised on the hook thread; the click goes on to where it was aimed.</summary>
    public event EventHandler? ClickedOutside;

    /// <summary>Starts catching the popup's keys; <paramref name="hotkey"/>, pressed again, closes it.</summary>
    public void Start(HotkeyChord? hotkey)
    {
        var (modifiers, key) = hotkey is { } chord ? ((nint)chord.Modifiers, (nint)chord.VirtualKey) : (0, 0);
        NativeMethods.PostThreadMessage(_threadId, StartMessage, modifiers, key);
    }

    /// <summary>Stops catching keys; keys still held from the popup are kept from the app until let go.</summary>
    public void Stop() => NativeMethods.PostThreadMessage(_threadId, StopMessage, 0, 0);

    public void Dispose()
    {
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
        NativeMethods.PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        // Creates the thread's message queue before anyone posts to it.
        NativeMethods.PeekMessage(out _, 0, 0, 0, NoRemove);
        _ready.Set();
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
        {
            switch (message.Message)
            {
                case StartMessage:
                    OnStart(message.WParam, message.LParam);
                    break;
                case StopMessage:
                    OnStop();
                    break;
                case WmTimer when message.WParam == (nint)_lingerTimer:
                    Unhook();
                    break;
                default:
                    NativeMethods.DispatchMessage(&message);
                    break;
            }
        }
        Unhook();
    }

    private void OnStart(nint modifiers, nint key)
    {
        _hotkey = key == 0 ? null : new HotkeyChord((HotkeyModifiers)modifiers, (uint)key);
        _active = true;
        StopLingering();
        _heldBefore.Clear();
        for (var virtualKey = 1; virtualKey < 255; virtualKey++)
        {
            if (NativeMethods.GetAsyncKeyState(virtualKey) < 0 && !_taken.Contains((uint)virtualKey))
            {
                _heldBefore.Add((uint)virtualKey);
            }
        }
        if (_keyboardHook == 0)
        {
            _keyboardHook = NativeMethods.SetWindowsHookEx(
                KeyboardHook, (nint)(delegate* unmanaged[Stdcall]<int, nint, nint, nint>)&OnKeyboard, NativeMethods.GetModuleHandle(null), 0);
        }
        if (_mouseHook == 0)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(
                MouseHook, (nint)(delegate* unmanaged[Stdcall]<int, nint, nint, nint>)&OnMouse, NativeMethods.GetModuleHandle(null), 0);
        }
        if (_keyboardHook == 0 || _mouseHook == 0)
        {
            Trace.TraceWarning($"watching the popup's keys failed (Win32 error {Marshal.GetLastPInvokeError()})");
        }
    }

    private void OnStop()
    {
        _active = false;
        _heldBefore.Clear();
        if (_taken.Count == 0)
        {
            Unhook();
        }
        else if (_lingerTimer == 0)
        {
            _lingerTimer = NativeMethods.SetTimer(0, 0, LingerMilliseconds, 0);
        }
    }

    private void Unhook()
    {
        StopLingering();
        _taken.Clear();
        if (_keyboardHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        if (_mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    private void StopLingering()
    {
        if (_lingerTimer != 0)
        {
            NativeMethods.KillTimer(0, _lingerTimer);
            _lingerTimer = 0;
        }
    }

    /// <summary>Whether to keep the key event from the app in front.</summary>
    private bool TakeKey(uint message, NativeMethods.KeyboardHookInfo* info)
    {
        var virtualKey = info->VirtualKey;
        var down = message is WmKeyDown or WmSysKeyDown;
        if (!down)
        {
            _heldBefore.Remove(virtualKey);
            if (!_taken.Remove(virtualKey))
            {
                return false;
            }
            if (!_active && _taken.Count == 0)
            {
                Unhook();
            }
            return true;
        }
        if (!_active)
        {
            // A key from the popup still held after a pick: its repeats must not reach the app.
            return _taken.Contains(virtualKey);
        }
        if (_heldBefore.Contains(virtualKey))
        {
            // Repeats of a key held since before the popup opened, such as the shortcut's V.
            return !PopupKeys.IsModifier(virtualKey);
        }
        var modifiers = Modifiers();
        // A character sent as such carries it in place of a scan code.
        var text = virtualKey == PopupKeys.Packet ? ((char)info->ScanCode).ToString() : Text(virtualKey, info->ScanCode, modifiers);
        var command = PopupKeys.Classify(virtualKey, modifiers, text, _hotkey);
        if (command is not { } taken)
        {
            return false;
        }
        _taken.Add(virtualKey);
        KeyPressed?.Invoke(this, taken);
        return true;
    }

    private void NoteMouse(uint message, NativeMethods.MouseHookInfo* info)
    {
        if (!_active || message is not (0x0201 or 0x0204 or 0x0207 or 0x020B))
        {
            return;
        }
        // Clipo's windows include the popup's menus.
        var window = NativeMethods.WindowFromPoint(info->Point);
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId != _ownProcessId)
        {
            ClickedOutside?.Invoke(this, EventArgs.Empty);
        }
    }

    private static HotkeyModifiers Modifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VkShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }
        if (IsDown(VkControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }
        if (IsDown(VkMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }
        if (IsDown(VkLeftWindows) || IsDown(VkRightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => NativeMethods.GetAsyncKeyState(virtualKey) < 0;

    /// <summary>
    /// What <paramref name="virtualKey"/> types with <paramref name="modifiers"/>
    /// under the keyboard layout of the app in front, where the user is
    /// typing; empty for a key that types nothing or a dead key. Keeps the
    /// layout's dead-key state as it was.
    /// </summary>
    private static string Text(uint virtualKey, uint scanCode, HotkeyModifiers modifiers)
    {
        var withoutAltGr = modifiers & ~HotkeyModifiers.Shift;
        if (withoutAltGr != HotkeyModifiers.None && withoutAltGr != (HotkeyModifiers.Control | HotkeyModifiers.Alt))
        {
            return "";
        }
        var state = stackalloc byte[256];
        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            state[VkShift] = 0x80;
        }
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            state[VkControl] = 0x80;
            state[VkLeftControl] = 0x80;
            state[VkMenu] = 0x80;
            state[VkRightMenu] = 0x80;
        }
        state[VkCapital] = (byte)(NativeMethods.GetKeyState(VkCapital) & 1);
        var thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var layout = NativeMethods.GetKeyboardLayout(thread);
        var buffer = stackalloc char[8];
        var count = NativeMethods.ToUnicodeEx(virtualKey, scanCode, state, buffer, 8, KeepKeyboardState, layout);
        if (count <= 0)
        {
            return "";
        }
        var text = new string(buffer, 0, count);
        // Control characters, such as the one Ctrl+Alt makes on a layout without AltGr, are not search text.
        return text.Any(char.IsControl) ? "" : text;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        // An exception must not escape into Windows.
        try
        {
            if (code >= 0 && s_current is { } input && input.TakeKey((uint)wParam, (NativeMethods.KeyboardHookInfo*)lParam))
            {
                return 1;
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"handling a key for the popup failed: {e.GetType().Name}: {e.Message}");
        }
        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint OnMouse(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && s_current is { } input)
            {
                input.NoteMouse((uint)wParam, (NativeMethods.MouseHookInfo*)lParam);
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"handling a click for the popup failed: {e.GetType().Name}: {e.Message}");
        }
        return NativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }
}
