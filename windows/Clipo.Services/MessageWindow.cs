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
/// An invisible window, message-only unless asked otherwise, that receives
/// what Windows sends the app: clipboard changes, shortcut presses and tray
/// icon events. Create it on the UI thread, which must run a message loop
/// (WinUI does), and dispose it there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class MessageWindow : IDisposable
{
    private const string ClassName = "io.github.arash7l3.Clipo.MessageWindow";
    private const uint PopupStyle = 0x80000000;
    private const uint ToolWindowStyle = 0x80;

    /// <summary>Windows are found by handle, because the window procedure is a plain function.</summary>
    private static readonly Dictionary<nint, MessageWindow> s_windows = [];

    private static bool s_classRegistered;

    /// <param name="topLevel">
    /// A hidden top-level window instead of a message-only one. Only a
    /// top-level window can come to the front, which a tray menu's owner must.
    /// It has no taskbar button and is never shown.
    /// </param>
    public MessageWindow(bool topLevel = false)
    {
        var instance = NativeMethods.GetModuleHandle(null);
        RegisterClass(instance);
        Handle = topLevel
            ? NativeMethods.CreateWindow(ToolWindowStyle, ClassName, "Clipo", PopupStyle, 0, 0, 0, 0, 0, 0, instance, 0)
            : NativeMethods.CreateWindow(0, ClassName, "Clipo", 0, 0, 0, 0, 0, NativeMethods.HwndMessage, 0, instance, 0);
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
