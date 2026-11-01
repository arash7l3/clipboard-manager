using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>
/// Remembers the last window the user worked in outside Clipo and the
/// taskbar. The tray icon is clicked with the taskbar in front, so a snippet
/// picked in its menu, or an item picked in the popup it opens, is pasted
/// into this window instead. Create it on the UI thread,
/// which must run a message loop (WinUI does), and dispose it there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class ForegroundTracker : IDisposable
{
    private const uint EventSystemForeground = 0x3;
    private const uint OutOfContext = 0x0;
    private const int ExtendedStyle = -20;

    /// <summary>Trackers are found by hook, because the callback is a plain function.</summary>
    private static readonly Dictionary<nint, ForegroundTracker> s_trackers = [];

    private nint _hook;

    public ForegroundTracker()
    {
        Note(NativeMethods.GetForegroundWindow());
        _hook = NativeMethods.SetWinEventHook(
            EventSystemForeground, EventSystemForeground, 0,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void>)&OnEvent,
            0, 0, OutOfContext);
        if (_hook == 0)
        {
            Trace.TraceWarning("watching the window in front failed; snippets paste into the window in front");
            return;
        }
        s_trackers[_hook] = this;
    }

    /// <summary>Raised on the UI thread with each window that comes to the front.</summary>
    public event EventHandler<nint>? Changed;

    /// <summary>The last app window that was in front, or 0 if none was seen yet.</summary>
    public nint LastAppWindow { get; private set; }

    /// <summary>
    /// The window in front if it is an app's, otherwise the last app window
    /// that was. A click on the tray icon puts the taskbar in front first, so
    /// the window in front alone would hand the focus back to the taskbar.
    /// </summary>
    public nint AppWindowInFront()
    {
        var window = NativeMethods.GetForegroundWindow();
        return IsAppWindow(window) ? window : LastAppWindow;
    }

    public void Dispose()
    {
        if (_hook == 0)
        {
            return;
        }
        s_trackers.Remove(_hook);
        NativeMethods.UnhookWinEvent(_hook);
        _hook = 0;
    }

    private void Note(nint window)
    {
        if (IsAppWindow(window))
        {
            LastAppWindow = window;
        }
    }

    private static bool IsAppWindow(nint window)
    {
        if (window == 0)
        {
            return false;
        }
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return ShellSurfaces.CountsAsApp(
            processId,
            (uint)Environment.ProcessId,
            Desktop.ClassName(window),
            NativeMethods.IsWindowVisible(window),
            NativeMethods.GetWindowLongPtr(window, ExtendedStyle));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        // An exception must not escape into Windows.
        try
        {
            if (kind == EventSystemForeground && s_trackers.TryGetValue(hook, out var tracker))
            {
                tracker.Note(window);
                tracker.Changed?.Invoke(tracker, window);
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"noting the window in front failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
