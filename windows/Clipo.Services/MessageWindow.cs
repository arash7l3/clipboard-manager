using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>A window message, as the window procedure received it.</summary>
public sealed class WindowMessageEventArgs(uint message, nint wParam, nint lParam) : EventArgs
{
    public uint Message { get; } = message;

    public nint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;
}

/// <summary>
/// An invisible, message-only window that receives what Windows sends the
/// app: clipboard changes, shortcut presses and tray icon events. Create it
/// on the UI thread, which must run a message loop (WinUI does), and dispose
/// it there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class MessageWindow : IDisposable
{
    private const string ClassName = "io.github.arash7l3.Clipo.MessageWindow";

    /// <summary>Windows are found by handle, because the window procedure is a plain function.</summary>
    private static readonly Dictionary<nint, MessageWindow> s_windows = [];

    private static bool s_classRegistered;

    public MessageWindow()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        RegisterClass(instance);
        Handle = NativeMethods.CreateWindow(0, ClassName, "Clipo", 0, 0, 0, 0, 0, NativeMethods.HwndMessage, 0, instance, 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"Could not create the message window (Win32 error {Marshal.GetLastPInvokeError()})");
        }
        s_windows[Handle] = this;
    }

    /// <summary>Raised on the creating thread for every message. Handlers must not throw.</summary>
    public event EventHandler<WindowMessageEventArgs>? MessageReceived;

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle == 0)
        {
            return;
        }
        s_windows.Remove(Handle);
        NativeMethods.DestroyWindow(Handle);
        Handle = 0;
    }

    private static void RegisterClass(nint instance)
    {
        if (s_classRegistered)
        {
            return;
        }
        fixed (char* className = ClassName)
        {
            var windowClass = new NativeMethods.WindowClass
            {
                Size = (uint)sizeof(NativeMethods.WindowClass),
                WindowProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProc,
                Instance = instance,
                ClassName = className,
            };
            if (NativeMethods.RegisterClass(&windowClass) == 0)
            {
                throw new InvalidOperationException($"Could not register the window class (Win32 error {Marshal.GetLastPInvokeError()})");
            }
        }
        s_classRegistered = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        // An exception must not escape into Windows.
        try
        {
            if (s_windows.TryGetValue(window, out var target))
            {
                target.MessageReceived?.Invoke(target, new WindowMessageEventArgs(message, wParam, lParam));
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"message {message:X} failed: {e.GetType().Name}: {e.Message}");
        }
        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }
}
