using System.Globalization;
using Clipo.Services;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipo;

/// <summary>
/// The parts of Clipo that live in Windows itself: the tray icon and its
/// menu, the global shortcut and the clipboard listener, which also owns
/// what Clipo writes to the clipboard. Create it on the UI
/// thread. Its events are raised on the UI thread, except
/// <see cref="ClipboardWatcher.Recorded"/>.
/// </summary>
internal sealed class Shell : IDisposable
{
    private const int OpenId = 1;
    private const int AutoPasteId = 2;
    private const int QuitId = 3;

    private static readonly ResourceLoader s_strings = new();

    private readonly MessageWindow _messages = new();
    private readonly TrayIcon _tray;
    private readonly GlobalHotkey _hotkey;

    public Shell()
    {
        _tray = new TrayIcon(_messages, Path.Combine(AppContext.BaseDirectory, "Clipo.ico"), s_strings.GetString("TrayTooltip"));
        _tray.Activated += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _tray.MenuRequested += OnMenuRequested;

        _hotkey = new GlobalHotkey(_messages);
        _hotkey.Pressed += (_, _) => HotkeyPressed?.Invoke(this, EventArgs.Empty);
        HotkeyRegistered = _hotkey.Register(HotkeyChord.ShowHistory);

        var rules = CaptureSettings.Rules();
        Watcher = new ClipboardWatcher(_messages, new ClipboardRecorder(new CaptureBuilder(new ImageProcessor())), () => rules);
        Watcher.Start();
    }

    /// <summary>The user clicked the tray icon or picked Show Clipboard History in its menu.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>The user pressed the shortcut.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>The user picked Paste After Choosing in the tray menu.</summary>
    public event EventHandler? AutoPasteToggled;

    /// <summary>The user picked Quit in the tray menu.</summary>
    public event EventHandler? QuitRequested;

    public ClipboardWatcher Watcher { get; }

    /// <summary>Whether the tray menu shows Paste After Choosing as checked.</summary>
    public bool AutoPaste { get; set; }

    /// <summary>The window that owns what Clipo writes to the clipboard.</summary>
    public nint ClipboardOwner => _messages.Handle;

    /// <summary>Whether the shortcut was registered; another app may hold it.</summary>
    public bool HotkeyRegistered { get; }

    public void Dispose()
    {
        Watcher.Store = null;
        Watcher.Dispose();
        _hotkey.Dispose();
        _tray.Dispose();
        _messages.Dispose();
    }

    private void OnMenuRequested(object? sender, TrayMenuEventArgs e)
    {
        var open = s_strings.GetString("MenuOpen");
        // After a tab, a menu shows the shortcut aligned to the side.
        var openText = _hotkey.Chord is { } chord ? $"{open}\t{chord.Display}" : open;
        var entries = new[]
        {
            new MenuEntry(OpenId, openText),
            new MenuEntry(AutoPasteId, s_strings.GetString("MenuAutoPaste"), IsChecked: AutoPaste),
            MenuEntry.Separator,
            new MenuEntry(QuitId, s_strings.GetString("MenuQuit")),
        };

        var picked = PopupMenu.Show(_messages, entries, e.X, e.Y, CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft);
        switch (picked)
        {
            case OpenId:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case AutoPasteId:
                AutoPasteToggled?.Invoke(this, EventArgs.Empty);
                break;
            case QuitId:
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
