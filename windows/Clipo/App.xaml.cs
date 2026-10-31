using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipo;

/// <summary>
/// Owns the status window, the popup, the tray icon and the open history.
/// Lives in the tray until Quit.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "Lives as long as the process; the store is disposed when the app quits.")]
public partial class App : Application
{
    /// <summary>Given to the copy that Settings starts to change the language: it waits for the old one to end.</summary>
    private const string RestartedArgument = "--restarted";

    /// <summary>How long a restarted copy waits for the old one to let go of the single-instance lock.</summary>
    private static readonly TimeSpan s_restartWait = TimeSpan.FromSeconds(10);

    /// <summary>Held for the app's lifetime; a second Clipo exits while it exists.</summary>
    private static Mutex? s_instance;

    private readonly ImageProcessor _images = new();
    private MainWindow? _window;
    private PopupWindow? _popup;
    private ItemRestorer? _restorer;
    private Shell? _shell;
    private SettingsWindow? _settingsWindow;
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private readonly AppLanguage _launchLanguage;
    private readonly LoginItem _login = new(new RegistryStartup(), Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Clipo.exe"));
    private readonly SemaphoreSlim _trimming = new(1, 1);
    private readonly CancellationTokenSource _quit = new();

    /// <summary>The capture rules in force; the clipboard listener reads them on its own thread.</summary>
    private volatile CaptureRules _rules = CaptureSettings.Rules();
    private DispatcherQueue? _dispatcher;
    private ClipStore? _store;

    public App()
    {
        // The language has to be known before anything loads a string.
        LoadSettings();
        _rules = CaptureSettings.Rules(_settings);
        _launchLanguage = _settings.Language;
        Appearance.ApplyLanguage(_launchLanguage);
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var restarted = arguments.Contains(RestartedArgument);
        // Started by Windows at sign-in, or by Settings: the tray icon is enough, no window.
        var quiet = restarted || arguments.Contains(LoginItem.BackgroundArgument);

        // Two copies would record every copy twice.
        if (!ClaimSingleInstance(restarted ? s_restartWait : TimeSpan.Zero))
        {
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var window = new MainWindow();
        _window = window;
        window.RetryRequested += (_, _) => _ = OpenHistoryAsync(window);

        _login.Repair();
        var popup = new PopupWindow(new PopupModel(query => _store?.Previews(query)));
        _popup = popup;
        popup.Chosen += (_, row) => _ = RestoreAsync(row);
        _restorer = new ItemRestorer(
            id => _store?.Get(id),
            WriteToClipboardAsync,
            id => _store?.Touch(id, DateTime.UtcNow),
            () => _settings.AutoPaste,
            () => Desktop.SendPaste());

        var chord = HotkeyChoices.Chord(_settings.Hotkey);
        var shell = new Shell(chord, () => _rules) { AutoPaste = _settings.AutoPaste };
        _shell = shell;
        shell.OpenRequested += (_, _) => ShowHistory(toggle: false);
        shell.HotkeyPressed += (_, _) => ShowHistory(toggle: true);
        shell.SettingsRequested += (_, _) => ShowSettings();
        shell.AutoPasteToggled += (_, _) => SetAutoPaste(!_settings.AutoPaste);
        shell.QuitRequested += (_, _) => Quit();
        shell.Watcher.Recorded += OnRecorded;
        if (!shell.HotkeyRegistered)
        {
            var format = new ResourceLoader().GetString("HotkeyUnavailable");
            window.ShowNotice(string.Format(CultureInfo.CurrentCulture, format, chord.Display));
        }

        ApplyTheme();
        if (!quiet)
        {
            window.Activate();
        }
        _ = OpenHistoryAsync(window);
        _ = TrimHourlyAsync();
    }

    /// <summary>Stops listening, closes the history and ends the app.</summary>
    private void Quit()
    {
        _quit.Cancel();
        _shell?.Dispose();
        _shell = null;
        _store?.Dispose();
        _store = null;
        _popup?.Quit();
        _settingsWindow?.Quit();
        _window?.Quit();
        Exit();
    }

    /// <summary>Starts a new copy of Clipo, which waits for this one to end, and quits. Used to change the language.</summary>
    private void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } executable)
            {
                Process.Start(new ProcessStartInfo(executable, RestartedArgument) { UseShellExecute = false })?.Dispose();
                Quit();
            }
        }
        catch (Win32Exception e)
        {
            Trace.TraceError($"restarting failed: {e.GetType().Name}");
        }
    }

    /// <summary>
    /// Opens the popup, or closes it when <paramref name="toggle"/> and it is
    /// open. While the history is not open, shows the status window instead,
    /// which says why and offers Try Again.
    /// </summary>
    private void ShowHistory(bool toggle)
    {
        if (_store is null || _popup is null)
        {
            _window?.ShowWindow();
        }
        else if (toggle)
        {
            _popup.Toggle();
        }
        else
        {
            _popup.Show();
        }
    }

    private async Task RestoreAsync(ItemPreview row)
    {
        if (_restorer is null || await _restorer.RestoreAsync(row.Id) == RestoreOutcome.Unavailable)
        {
            Desktop.Beep();
        }
    }

    /// <summary>
    /// Puts <paramref name="content"/> on the clipboard, an image with a
    /// bitmap copy for apps that cannot paste PNG, and tells the watcher not
    /// to record it as a new copy.
    /// </summary>
    private async Task<bool> WriteToClipboardAsync(ItemContent content)
    {
        var bitmap = content is ItemContent.Image image ? await _images.ToBitmapAsync(image.ImageValue.Data) : null;
        if (_shell is not { } shell)
        {
            return false;
        }
        var written = ClipboardWriter.Write(shell.ClipboardOwner, content, bitmap);
        // Even a failed write may have emptied the clipboard; either way it was Clipo.
        shell.Watcher.SkipCurrent();
        return written;
    }

    private void LoadSettings()
    {
        try
        {
            _settingsStore = new AppSettingsStore(AppSettingsStore.DefaultPath());
            _settings = _settingsStore.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No folder for the settings: run with the defaults, unsaved.
            Trace.TraceError($"no settings folder: {e.GetType().Name}");
        }
    }

    /// <summary>Opens the Settings window, creating it the first time.</summary>
    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            var window = new SettingsWindow(_settings, _launchLanguage, _login);
            window.ApplyTheme(_settings.Theme);
            window.Changed += (_, settings) => ApplySettings(settings);
            window.RestartRequested += (_, _) => Restart();
            _settingsWindow = window;
        }
        _settingsWindow.ShowWindow();
    }

    private void SetAutoPaste(bool on)
    {
        ApplySettings(_settings with { AutoPaste = on });
        _settingsWindow?.Reflect(_settings);
    }

    /// <summary>
    /// Saves <paramref name="requested"/> and puts each change to work: the
    /// shortcut, the theme, the capture rules and the history's limits. A
    /// shortcut that another app holds is undone, and Settings says so.
    /// </summary>
    private void ApplySettings(AppSettings requested)
    {
        var before = _settings;
        var next = requested;
        var hotkeyRefused = false;
        if (next.Hotkey != before.Hotkey && _shell is { } shell && !shell.ChangeHotkey(HotkeyChoices.Chord(next.Hotkey)))
        {
            next = next with { Hotkey = before.Hotkey };
            hotkeyRefused = true;
            Desktop.Beep();
        }

        _settings = next;
        _settingsStore?.Save(next);
        _rules = CaptureSettings.Rules(next);
        if (_shell is { } current)
        {
            current.AutoPaste = next.AutoPaste;
        }
        if (next.Theme != before.Theme)
        {
            ApplyTheme();
        }
        if (!next.HasSameRetentionAs(before))
        {
            _ = TrimAsync();
        }
        if (hotkeyRefused)
        {
            _settingsWindow?.Reflect(next, hotkeyRefused: true);
        }
    }

    private void ApplyTheme()
    {
        _window?.ApplyTheme(_settings.Theme);
        _popup?.ApplyTheme(_settings.Theme);
        _settingsWindow?.ApplyTheme(_settings.Theme);
    }

    /// <summary>
    /// Gives the open history the current limits and rules and deletes what
    /// has expired. One at a time, each with the settings as they are when it
    /// starts, so the last change always wins.
    /// </summary>
    private async Task TrimAsync()
    {
        await _trimming.WaitAsync();
        try
        {
            if (_store is not { } store)
            {
                return;
            }
            var settings = _settings;
            var count = await Task.Run(() => CountAfterApplying(store, settings));
            if (count is { } itemCount && ReferenceEquals(store, _store))
            {
                _window?.ShowReady(itemCount, announce: false);
                _popup?.HistoryChanged();
            }
        }
        finally
        {
            _trimming.Release();
        }
    }

    /// <summary>Once an hour, so items expire while Clipo keeps running.</summary>
    private async Task TrimHourlyAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            while (await timer.WaitForNextTickAsync(_quit.Token))
            {
                await TrimAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Quitting.
        }
    }

    /// <summary>Applies the settings to <paramref name="store"/> and counts its items; <c>null</c> if that failed, for example because it was closed meanwhile.</summary>
    private static ulong? CountAfterApplying(ClipStore store, AppSettings settings)
    {
        try
        {
            return HistorySettings.Apply(store, settings, DateTime.UtcNow) ? store.Count() : null;
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"counting the history failed: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>Runs on a background thread after a copy was kept.</summary>
    private void OnRecorded(object? sender, ClipboardRecordedEventArgs e)
    {
        try
        {
            var count = _store?.Count();
            if (count is { } itemCount)
            {
                _dispatcher?.TryEnqueue(() =>
                {
                    _window?.ShowReady(itemCount, announce: false);
                    _popup?.HistoryChanged();
                });
            }
        }
        catch (Exception ex)
        {
            // The history may have been closed meanwhile.
            Trace.TraceWarning($"counting the history failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Takes the lock that keeps a second Clipo out, waiting up to
    /// <paramref name="wait"/> for another copy to let go of it.
    /// </summary>
    private static bool ClaimSingleInstance(TimeSpan wait)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            // Local\ is per sign-in session, so each Windows user gets their own Clipo.
            var mutex = new Mutex(initiallyOwned: true, @"Local\io.github.arash7l3.Clipo", out var createdNew);
            if (createdNew)
            {
                s_instance = mutex;
                return true;
            }
            mutex.Dispose();
            if (waited.Elapsed >= wait)
            {
                return false;
            }
            Thread.Sleep(200);
        }
    }

    private async Task OpenHistoryAsync(MainWindow window)
    {
        window.ShowOpening();
        switch (await History.OpenDefaultAsync())
        {
            case OpenResult.Ready ready:
                if (await UseStoreAsync(ready.Store) is { } itemCount)
                {
                    window.ShowReady(itemCount);
                }
                else
                {
                    window.ShowUnavailable();
                }
                break;
            case OpenResult.KeyProblem:
                window.ShowUnavailable();
                await OfferNewHistoryAsync(window);
                break;
            default:
                window.ShowUnavailable();
                break;
        }
    }

    /// <summary>
    /// Makes <paramref name="store"/> the open history and starts recording
    /// into it. Returns how many items it holds after the settings were
    /// applied, or <c>null</c>, with the store closed, if they could not be:
    /// nothing is captured then, rather than everything.
    /// </summary>
    private async Task<ulong?> UseStoreAsync(ClipStore store)
    {
        if (_shell is { } shell)
        {
            shell.Watcher.Store = null;
        }
        _store?.Dispose();
        _store = null;

        // Off the UI thread: expired items are deleted. Nothing is recorded until the rules are set.
        var settings = _settings;
        var count = await Task.Run(() => CountAfterApplying(store, settings));
        if (count is null)
        {
            store.Dispose();
            return null;
        }
        _store = store;
        if (_shell is { } current)
        {
            current.Watcher.Store = store;
        }
        return count;
    }

    /// <summary>The core never replaces a key by itself: the user decides whether to give up the old history.</summary>
    private async Task OfferNewHistoryAsync(MainWindow window)
    {
        if (await window.ConfirmStartOverAsync() && await History.StartOverDefaultAsync())
        {
            await OpenHistoryAsync(window);
        }
    }
}
