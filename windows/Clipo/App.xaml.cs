using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Clipo;

/// <summary>Owns the status window, the tray icon and the open history. Lives in the tray until Quit.</summary>
[SuppressMessage("Design", "CA1001", Justification = "Lives as long as the process; the store is disposed when the app quits.")]
public partial class App : Application
{
    /// <summary>Held for the app's lifetime; a second Clipo exits while it exists.</summary>
    private static Mutex? s_instance;

    private MainWindow? _window;
    private Shell? _shell;
    private DispatcherQueue? _dispatcher;
    private ClipStore? _store;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Two copies would record every copy twice.
        if (!ClaimSingleInstance())
        {
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var window = new MainWindow();
        _window = window;
        window.RetryRequested += (_, _) => _ = OpenHistoryAsync(window);

        var shell = new Shell();
        _shell = shell;
        shell.OpenRequested += (_, _) => window.ShowWindow();
        shell.QuitRequested += (_, _) => Quit();
        shell.Watcher.Recorded += OnRecorded;
        if (!shell.HotkeyRegistered)
        {
            var format = new ResourceLoader().GetString("HotkeyUnavailable");
            window.ShowNotice(string.Format(CultureInfo.CurrentCulture, format, HotkeyChord.ShowHistory.Display));
        }

        window.Activate();
        _ = OpenHistoryAsync(window);
    }

    /// <summary>Stops listening, closes the history and ends the app.</summary>
    private void Quit()
    {
        _shell?.Dispose();
        _shell = null;
        _store?.Dispose();
        _store = null;
        _window?.Quit();
        Exit();
    }

    /// <summary>Runs on a background thread after a copy was kept.</summary>
    private void OnRecorded(object? sender, ClipboardRecordedEventArgs e)
    {
        try
        {
            var count = _store?.Count();
            if (count is { } itemCount)
            {
                _dispatcher?.TryEnqueue(() => _window?.ShowReady(itemCount, announce: false));
            }
        }
        catch (Exception ex)
        {
            // The history may have been closed meanwhile.
            Trace.TraceWarning($"counting the history failed: {ex.GetType().Name}");
        }
    }

    private static bool ClaimSingleInstance()
    {
        // Local\ is per sign-in session, so each Windows user gets their own Clipo.
        var mutex = new Mutex(initiallyOwned: true, @"Local\io.github.arash7l3.Clipo", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }
        s_instance = mutex;
        return true;
    }

    private async Task OpenHistoryAsync(MainWindow window)
    {
        window.ShowOpening();
        switch (await History.OpenDefaultAsync())
        {
            case OpenResult.Ready ready:
                if (UseStore(ready.Store))
                {
                    window.ShowReady(ready.ItemCount);
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
    /// into it. Returns false, with the store closed, if the capture rules
    /// could not be set: nothing is captured then, rather than everything.
    /// </summary>
    private bool UseStore(ClipStore store)
    {
        if (_shell is { } shell)
        {
            shell.Watcher.Store = null;
        }
        _store?.Dispose();
        _store = null;
        try
        {
            store.SetRules(CaptureSettings.Rules());
        }
        catch (Exception e)
        {
            Trace.TraceError($"setting the capture rules failed: {e.GetType().Name}: {e.Message}");
            store.Dispose();
            return false;
        }
        _store = store;
        if (_shell is { } current)
        {
            current.Watcher.Store = store;
        }
        return true;
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
