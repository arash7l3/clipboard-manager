using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>Where the user asked for the tray menu, in screen pixels.</summary>
public sealed class TrayMenuEventArgs(int x, int y) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;
}

/// <summary>
/// The icon in the notification area. It reports clicks; the app decides
/// what they do. It is added again when Explorer restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIcon : IDisposable
{
    private const uint Id = 1;
    private const uint Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const uint FlagMessage = 0x1, FlagIcon = 0x2, FlagTip = 0x4, FlagShowTip = 0x80;
    private const uint Version4 = 4;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x10;
    private const int SmallIconWidth = 49;
    private const int TipLength = 128;
    private const uint SelectEvent = 0x400, KeySelectEvent = 0x401;
    private const uint CallbackMessage = NativeMethods.WmApp + 1;

    private readonly MessageWindow _window;
    private string _tooltip;
    private readonly uint _taskbarCreated;
    private nint _icon;
    private bool _added;

    /// <param name="window">Receives the icon's events.</param>
    /// <param name="iconPath">An .ico file.</param>
    /// <param name="tooltip">Shown on hover and read by screen readers; truncated to 127 characters.</param>
    public TrayIcon(MessageWindow window, string iconPath, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrEmpty(iconPath);
        _window = window;
        _tooltip = Shorten(tooltip);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _icon = Load(iconPath);

        _window.MessageReceived += OnMessage;
        Install();
    }

    /// <summary>The icon was clicked, or selected with the keyboard.</summary>
    public event EventHandler? Activated;

    /// <summary>The user asked for the context menu.</summary>
    public event EventHandler<TrayMenuEventArgs>? MenuRequested;

    /// <summary>Whether Windows accepted the icon.</summary>
    public bool IsVisible => _added;

    /// <summary>Shows another icon and tooltip, for example while private mode is on.</summary>
    /// <param name="iconPath">An .ico file.</param>
    /// <param name="tooltip">Shown on hover and read by screen readers.</param>
    public void SetAppearance(string iconPath, string tooltip)
    {
        ArgumentException.ThrowIfNullOrEmpty(iconPath);
        var icon = Load(iconPath);
        if (icon == 0)
        {
            // Keep the icon that works.
            return;
        }
        var old = _icon;
        _icon = icon;
        _tooltip = Shorten(tooltip);
        if (_added)
        {
            Notify(Modify);
        }
        if (old != 0)
        {
            NativeMethods.DestroyIcon(old);
        }
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        if (_added)
        {
            Notify(Delete);
            _added = false;
        }
        if (_icon != 0)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private static string Shorten(string tooltip) => tooltip.Length < TipLength ? tooltip : tooltip[..(TipLength - 1)];

    private nint Load(string iconPath)
    {
        var dpi = NativeMethods.GetDpiForWindow(_window.Handle);
        var size = NativeMethods.GetSystemMetricsForDpi(SmallIconWidth, dpi == 0 ? 96 : dpi);
        var icon = NativeMethods.LoadImage(0, iconPath, ImageIcon, size, size, LoadFromFile);
        if (icon == 0)
        {
            Trace.TraceError($"loading the tray icon failed (Win32 error {Marshal.GetLastPInvokeError()})");
        }
        return icon;
    }

    private void Install()
    {
        _added = Notify(Add);
        if (!_added)
        {
            Trace.TraceError("adding the tray icon failed");
            return;
        }
        // Version 4 reports keyboard use and gives the click position.
        Notify(SetVersion);
    }

    private unsafe bool Notify(uint command)
    {
        var data = new NativeMethods.NotifyIconData
        {
            Size = (uint)sizeof(NativeMethods.NotifyIconData),
            Window = _window.Handle,
            Id = Id,
            Flags = FlagMessage | FlagIcon | FlagTip | FlagShowTip,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
            Version = Version4,
        };
        _tooltip.AsSpan().CopyTo(data.Tip);
        return NativeMethods.ShellNotifyIcon(command, &data);
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message == _taskbarCreated)
        {
            // Explorer restarted and lost every icon.
            Install();
            return;
        }
        if (e.Message != CallbackMessage)
        {
            return;
        }
        switch ((uint)((ulong)e.LParam & 0xFFFF))
        {
            case SelectEvent or KeySelectEvent:
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WmContextMenu:
                // Version 4 puts the position, in screen pixels, in wParam.
                var x = (short)((ulong)e.WParam & 0xFFFF);
                var y = (short)(((ulong)e.WParam >> 16) & 0xFFFF);
                MenuRequested?.Invoke(this, new TrayMenuEventArgs(x, y));
                break;
        }
    }
}
