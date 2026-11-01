using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>A point on the screen, in physical pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A rectangle on the screen, in physical pixels; right and bottom are exclusive.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>The mouse pointer and the monitor under it.</summary>
/// <param name="Cursor">Where the pointer is.</param>
/// <param name="WorkArea">The monitor's area without the taskbar.</param>
/// <param name="Dpi">The monitor's scale: 96 is 100 %.</param>
public readonly record struct PointerScreen(ScreenPoint Cursor, ScreenRect WorkArea, uint Dpi);

/// <summary>Focus, keys and screens: what the popup needs from the desktop around it.</summary>
[SupportedOSPlatform("windows")]
public static unsafe class Desktop
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;
    private const int VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const int ExtendedStyle = -20;
    private const long NoActivate = 0x08000000;
    private const nint Topmost = -1;
    private const uint NoSize = 0x1;
    private const uint NoMove = 0x2;
    private const uint NoActivateWindow = 0x10;
    private const int ClassNameLength = 256;

    /// <summary>The window the user is working in.</summary>
    public static nint ForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>Whether <paramref name="window"/> is one of Clipo's own.</summary>
    public static bool IsOwnWindow(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return window != 0 && processId == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Keeps clicks on <paramref name="window"/> from activating it, so the
    /// app the user works in keeps the focus.
    /// </summary>
    public static void MakeNonActivating(nint window)
    {
        var style = (long)NativeMethods.GetWindowLongPtr(window, ExtendedStyle);
        if ((style & NoActivate) == 0)
        {
            NativeMethods.SetWindowLongPtr(window, ExtendedStyle, (nint)(style | NoActivate));
        }
    }

    /// <summary>Puts <paramref name="window"/> above every other always-on-top window, without activating it.</summary>
    public static void BringToTop(nint window) =>
        NativeMethods.SetWindowPos(window, Topmost, 0, 0, 0, 0, NoMove | NoSize | NoActivateWindow);

    /// <summary>
    /// Whether <paramref name="window"/> shows a folder, such as Explorer or
    /// the desktop: a paste there takes files, not pictures.
    /// </summary>
    public static bool ShowsFolder(nint window)
    {
        if (window == 0 || !NativeMethods.IsWindow(window))
        {
            return false;
        }
        var found = false;
        NativeMethods.EnumChildWindows(window, (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&FindFolderView, (nint)(&found));
        return ShellSurfaces.ShowsFolder(ClassName(window), found);
    }

    /// <summary>The window class of <paramref name="window"/>, or <c>null</c>.</summary>
    internal static string? ClassName(nint window)
    {
        var name = stackalloc char[ClassNameLength];
        var length = NativeMethods.GetClassName(window, name, ClassNameLength);
        return length > 0 ? new string(name, 0, length) : null;
    }

    /// <summary>
    /// Brings <paramref name="window"/> back to the front, if it still exists.
    /// Windows allows it only while this app is in front, so call it before
    /// hiding the popup.
    /// </summary>
    public static void Activate(nint window)
    {
        if (window != 0 && NativeMethods.IsWindow(window))
        {
            NativeMethods.SetForegroundWindow(window);
        }
    }

    /// <summary>
    /// Whether a Shift key is held down now, for a Shift+click. The popup
    /// never has the keyboard, so its thread's own key state is out of date.
    /// </summary>
    public static bool IsShiftDown() => NativeMethods.GetAsyncKeyState(VkShift) < 0;

    /// <summary>The default "something went wrong" sound.</summary>
    public static void Beep() => NativeMethods.MessageBeep(0xFFFFFFFF);

    /// <summary>
    /// Presses Ctrl+V in the window in front. The virtual key follows the
    /// keyboard layout, so it is Ctrl+V on Dvorak too. Windows drops it
    /// silently when the window in front runs as administrator and Clipo does not.
    /// </summary>
    public static bool SendPaste()
    {
        var inputs = stackalloc NativeMethods.Input[4];
        inputs[0] = Key(VkControl, up: false);
        inputs[1] = Key(VkV, up: false);
        inputs[2] = Key(VkV, up: true);
        inputs[3] = Key(VkControl, up: true);
        return NativeMethods.SendInput(4, inputs, sizeof(NativeMethods.Input)) == 4;
    }

    /// <summary>The pointer's position and the monitor it is on.</summary>
    public static PointerScreen PointerScreen()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = (uint)sizeof(NativeMethods.MonitorInfo) };
        var work = NativeMethods.GetMonitorInfo(monitor, &info)
            ? new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom)
            : new ScreenRect(cursor.X - 400, cursor.Y - 300, cursor.X + 400, cursor.Y + 300);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX != 0 ? dpiX : 96;
        return new PointerScreen(new ScreenPoint(cursor.X, cursor.Y), work, dpi);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int FindFolderView(nint child, nint found)
    {
        // An exception must not escape into Windows.
        try
        {
            if (ClassName(child) == ShellSurfaces.FolderViewClass)
            {
                *(bool*)found = true;
                return 0;
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"looking for a folder view failed: {e.GetType().Name}");
        }
        return 1;
    }

    private static NativeMethods.Input Key(ushort virtualKey, bool up) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput { VirtualKey = virtualKey, Flags = up ? NativeMethods.KeyEventKeyUp : 0 },
        },
    };
}
