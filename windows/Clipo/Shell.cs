using System.Globalization;
using Clipo.Core;
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
    private const int SettingsId = 2;
    private const int AutoPasteId = 3;
    private const int QuitId = 4;
    private const int PrivateModeId = 5;

    /// <summary>Snippet menu items are numbered from here, in list order.</summary>
    private const int SnippetBaseId = 100;

    private static readonly ResourceLoader s_strings = new();

    private readonly MessageWindow _messages = new();
    private readonly TrayIcon _tray;
    private readonly GlobalHotkey _hotkey;
    private bool _privateMode;

    /// <param name="chord">The shortcut that opens the history.</param>
    /// <param name="rules">The capture rules in force, asked before every copy is read.</param>
    public Shell(HotkeyChord chord, Func<CaptureRules> rules)
    {
        _tray = new TrayIcon(_messages, IconPath(isPrivate: false), s_strings.GetString("TrayTooltip"));
        _tray.Activated += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _tray.MenuRequested += OnMenuRequested;

        _hotkey = new GlobalHotkey(_messages);
        _hotkey.Pressed += (_, _) => HotkeyPressed?.Invoke(this, EventArgs.Empty);
        HotkeyRegistered = _hotkey.Register(chord);

        Watcher = new ClipboardWatcher(_messages, new ClipboardRecorder(new CaptureBuilder(new ImageProcessor())), rules);
        Watcher.Start();
    }

    /// <summary>The user clicked the tray icon or picked Show Clipboard History in its menu.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>The user pressed the shortcut.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>The user picked Settings in the tray menu.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>The user picked Paste After Choosing in the tray menu.</summary>
    public event EventHandler? AutoPasteToggled;

    /// <summary>The user picked a snippet in the tray menu's Snippets submenu.</summary>
    public event EventHandler<Snippet>? SnippetChosen;

    /// <summary>The user picked Private Mode in the tray menu.</summary>
    public event EventHandler? PrivateModeToggled;

    /// <summary>The user picked Quit in the tray menu.</summary>
    public event EventHandler? QuitRequested;

    public ClipboardWatcher Watcher { get; }

    /// <summary>Whether the tray menu shows Paste After Choosing as checked.</summary>
    public bool AutoPaste { get; set; }

    /// <summary>The snippets the tray menu lists, in order.</summary>
    public IReadOnlyList<Snippet> Snippets { get; set; } = [];

    /// <summary>
    /// Whether private mode is on: the tray menu shows it checked, and the
    /// tray icon and its tooltip change so it shows at a glance.
    /// </summary>
    public bool PrivateMode
    {
        get => _privateMode;
        set
        {
            if (_privateMode == value)
            {
                return;
            }
            _privateMode = value;
            _tray.SetAppearance(
                IconPath(value),
                s_strings.GetString(value ? "TrayTooltipPrivate" : "TrayTooltip"));
        }
    }

    /// <summary>The window that owns what Clipo writes to the clipboard.</summary>
    public nint ClipboardOwner => _messages.Handle;

    /// <summary>Whether the shortcut was registered; another app may hold it.</summary>
    public bool HotkeyRegistered { get; }

    /// <summary>
    /// Moves the shortcut to <paramref name="chord"/>. Returns whether it
    /// worked; if another app holds the new one, the old one is registered again.
    /// </summary>
    public bool ChangeHotkey(HotkeyChord chord)
    {
        var previous = _hotkey.Chord;
        if (_hotkey.Register(chord))
        {
            return true;
        }
        if (previous is { } old)
        {
            _hotkey.Register(old);
        }
        return false;
    }

    public void Dispose()
    {
        Watcher.Store = null;
        Watcher.Dispose();
        _hotkey.Dispose();
        _tray.Dispose();
        _messages.Dispose();
    }

    private static string IconPath(bool isPrivate) =>
        Path.Combine(AppContext.BaseDirectory, isPrivate ? "ClipoPrivate.ico" : "Clipo.ico");

    private void OnMenuRequested(object? sender, TrayMenuEventArgs e)
    {
        var open = s_strings.GetString("MenuOpen");
        // After a tab, a menu shows the shortcut aligned to the side.
        var openText = _hotkey.Chord is { } chord ? $"{open}\t{chord.Display}" : open;
        var snippets = Snippets;
        List<MenuEntry> snippetEntries = snippets.Count == 0
            ? [new MenuEntry(SnippetBaseId - 1, s_strings.GetString("MenuNoSnippets"), IsEnabled: false)]
            : snippets.Select((snippet, index) => new MenuEntry(SnippetBaseId + index, Extras.MenuTitle(snippet.Title))).ToList();
        var entries = new[]
        {
            new MenuEntry(OpenId, openText),
            new MenuEntry(0, s_strings.GetString("MenuSnippets"), Submenu: snippetEntries),
            new MenuEntry(SettingsId, s_strings.GetString("MenuSettings")),
            new MenuEntry(AutoPasteId, s_strings.GetString("MenuAutoPaste"), IsChecked: AutoPaste),
            new MenuEntry(PrivateModeId, s_strings.GetString("MenuPrivateMode"), IsChecked: PrivateMode),
            MenuEntry.Separator,
            new MenuEntry(QuitId, s_strings.GetString("MenuQuit")),
        };

        var picked = PopupMenu.Show(_messages, entries, e.X, e.Y, CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft);
        switch (picked)
        {
            case OpenId:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SettingsId:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case AutoPasteId:
                AutoPasteToggled?.Invoke(this, EventArgs.Empty);
                break;
            case PrivateModeId:
                PrivateModeToggled?.Invoke(this, EventArgs.Empty);
                break;
            case QuitId:
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
            case >= SnippetBaseId when picked - SnippetBaseId < snippets.Count:
                SnippetChosen?.Invoke(this, snippets[picked - SnippetBaseId]);
                break;
        }
    }
}
