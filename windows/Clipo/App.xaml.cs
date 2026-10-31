using System.Diagnostics.CodeAnalysis;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Xaml;

namespace Clipo;

/// <summary>Owns the status window and the open history.</summary>
[SuppressMessage("Design", "CA1001", Justification = "Lives as long as the process; the store is disposed when the window closes.")]
public partial class App : Application
{
    /// <summary>Held for the app's lifetime; a second Clipo exits while it exists.</summary>
    private static Mutex? s_instance;

    private MainWindow? _window;
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

        _window = new MainWindow();
        _window.RetryRequested += (_, _) => _ = OpenHistoryAsync(_window);
        _window.Closed += (_, _) =>
        {
            _store?.Dispose();
            _store = null;
        };
        _window.Activate();
        _ = OpenHistoryAsync(_window);
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
                _store?.Dispose();
                _store = ready.Store;
                window.ShowReady(ready.ItemCount);
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

    /// <summary>The core never replaces a key by itself: the user decides whether to give up the old history.</summary>
    private async Task OfferNewHistoryAsync(MainWindow window)
    {
        if (await window.ConfirmStartOverAsync() && await History.StartOverDefaultAsync())
        {
            await OpenHistoryAsync(window);
        }
    }
}
