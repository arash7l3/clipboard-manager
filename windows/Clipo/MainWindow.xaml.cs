using System.Globalization;
using Clipo.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace Clipo;

/// <summary>
/// Shows the state of the history, and asks before starting a new one or
/// clearing it.
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
        Root.FlowDirection = Appearance.Direction;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Clipo.ico"));
        AppWindow.Closing += OnClosing;
        Resize(s_statusSize);
        ShowOpening();
    }

    /// <summary>Gives the window the theme the user chose.</summary>
    public void ApplyTheme(AppTheme theme) => Appearance.ApplyTheme(Root, theme);

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
    public Task<bool> ConfirmStartOverAsync() =>
        ConfirmAsync("KeyProblemTitle", "KeyProblemText", "StartNewHistory", "NotNow");

    /// <summary>Asks whether to delete every unpinned item, after Clear History in the tray menu.</summary>
    public Task<bool> ConfirmClearHistoryAsync() =>
        ConfirmAsync("ClearHistoryTitle", "ClearHistoryText", "ClearHistoryButton", "Cancel");

    /// <summary>Shows the window with a dialog of these strings; <c>true</c> if the user picked <paramref name="primary"/>.</summary>
    private async Task<bool> ConfirmAsync(string title, string text, string primary, string close)
    {
        ShowWindow();
        await LoadedAsync();
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = s_strings.GetString(title),
            Content = s_strings.GetString(text),
            PrimaryButtonText = s_strings.GetString(primary),
            CloseButtonText = s_strings.GetString(close),
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

    private void Resize(SizeInt32 size) => WindowSize.Resize(AppWindow, size);
}
