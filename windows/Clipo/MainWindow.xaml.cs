using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace Clipo;

/// <summary>Shows the state of the history, and asks before starting a new one.</summary>
public sealed partial class MainWindow : Window
{
    private static readonly ResourceLoader s_strings = new();

    /// <summary>The status window's size, in effective pixels.</summary>
    private static readonly SizeInt32 s_statusSize = new(440, 240);

    /// <summary>
    /// Room for the start-over dialog, which is laid out inside the window:
    /// its text, longer in Farsi, was cut off at the status size.
    /// </summary>
    private static readonly SizeInt32 s_dialogSize = new(560, 440);

    public MainWindow()
    {
        InitializeComponent();
        Resize(s_statusSize);
        ShowOpening();
    }

    /// <summary>Raised when the user picks Try Again after the history failed to open.</summary>
    public event EventHandler? RetryRequested;

    public void ShowOpening() => ShowStatus(s_strings.GetString("StatusOpening"), canRetry: false);

    public void ShowReady(ulong itemCount)
    {
        var format = s_strings.GetString(itemCount == 1 ? "StatusReadyOne" : "StatusReadyOther");
        ShowStatus(string.Format(CultureInfo.CurrentCulture, format, itemCount), canRetry: false);
    }

    public void ShowUnavailable() => ShowStatus(s_strings.GetString("StatusUnavailable"), canRetry: true);

    /// <summary>Asks whether to delete the unreadable history and start a new one.</summary>
    public async Task<bool> ConfirmStartOverAsync()
    {
        await LoadedAsync();
        Activate();
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = s_strings.GetString("KeyProblemTitle"),
            Content = s_strings.GetString("KeyProblemText"),
            PrimaryButtonText = s_strings.GetString("StartNewHistory"),
            CloseButtonText = s_strings.GetString("NotNow"),
            // The safe choice is the default.
            DefaultButton = ContentDialogButton.Close,
        };
        Resize(s_dialogSize);
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            Resize(s_statusSize);
        }
    }

    private void ShowStatus(string text, bool canRetry)
    {
        StatusText.Text = text;
        TryAgainButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        FrameworkElementAutomationPeer.CreatePeerForElement(StatusText)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void OnTryAgain(object sender, RoutedEventArgs e) => RetryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Completes once the content has a XamlRoot, which dialogs need.</summary>
    private Task LoadedAsync()
    {
        if (Root.XamlRoot is not null)
        {
            return Task.CompletedTask;
        }
        var loaded = new TaskCompletionSource();
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= OnLoaded;
            loaded.SetResult();
        }
        Root.Loaded += OnLoaded;
        return loaded.Task;
    }

    /// <summary>Sets the size in effective pixels, scaled for the monitor's DPI.</summary>
    private void Resize(SizeInt32 size)
    {
        var dpi = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32((int)(size.Width * scale), (int)(size.Height * scale)));
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
