using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace Clipo;

/// <summary>
/// Shows the state of the history, and asks before starting a new one.
/// Closing the window hides it, because the app lives in the tray.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly ResourceLoader s_strings = new();

    /// <summary>The status window's size, in effective pixels.</summary>
    private static readonly SizeInt32 s_statusSize = new(440, 300);

    /// <summary>
    /// Room for the start-over dialog, which is laid out inside the window:
    /// its text, longer in Farsi, was cut off at the status size.
    /// </summary>
    private static readonly SizeInt32 s_dialogSize = new(560, 440);

    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Clipo.ico"));
        AppWindow.Closing += OnClosing;
        Resize(s_statusSize);
        ShowOpening();
    }

    /// <summary>Raised when the user picks Try Again after the history failed to open.</summary>
    public event EventHandler? RetryRequested;

    public void ShowOpening() => ShowStatus(s_strings.GetString("StatusOpening"), canRetry: false);

    /// <param name="announce">Whether screen readers read the change out; not for the count growing after each copy.</param>
    public void ShowReady(ulong itemCount, bool announce = true)
    {
        var format = s_strings.GetString(itemCount == 1 ? "StatusReadyOne" : "StatusReadyOther");
        ShowStatus(string.Format(CultureInfo.CurrentCulture, format, itemCount), canRetry: false, announce);
    }

    public void ShowUnavailable() => ShowStatus(s_strings.GetString("StatusUnavailable"), canRetry: true);

    /// <summary>A line under the status, such as a shortcut that could not be registered; <c>null</c> removes it.</summary>
    public void ShowNotice(string? text)
    {
        NoticeText.Text = text ?? "";
        NoticeText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Brings the window to the front, showing it if it was hidden.</summary>
    public void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>Closes the window for good, which ends the hiding.</summary>
    public void Quit()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Asks whether to delete the unreadable history and start a new one.</summary>
    public async Task<bool> ConfirmStartOverAsync()
    {
        ShowWindow();
        await LoadedAsync();
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

    private void ShowStatus(string text, bool canRetry, bool announce = true)
    {
        StatusText.Text = text;
        TryAgainButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        if (announce)
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(StatusText)
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
            sender.Hide();
        }
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
