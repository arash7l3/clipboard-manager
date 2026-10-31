using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace Clipo;

/// <summary>
/// Clipo's settings. Each control changes its setting at once and tells the
/// app through <see cref="Changed"/>, which applies and saves it and may hand
/// the settings back with <see cref="Reflect"/>, for example to undo a
/// shortcut that another app holds. Closing the window hides it.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>The window's size, in effective pixels.</summary>
    private static readonly SizeInt32 s_size = new(560, 640);

    private static readonly ResourceLoader s_strings = new();

    /// <summary>The largest export file Import reads, in bytes. Real exports are far smaller; this keeps a wrong pick from filling memory.</summary>
    private const ulong MaxImportBytes = 512UL * 1024 * 1024;

    private const string ExportExtension = ".clipoexport";

    private readonly LoginItem _login;
    private readonly Extras _extras;
    private List<Snippet> _snippets = [];
    private long? _editingSnippet;
    private readonly AppLanguage _launchLanguage;
    private AppSettings _settings;

    /// <summary>Set while the controls are being filled in, so that does not count as the user changing them.</summary>
    private bool _loading;
    private bool _allowClose;

    /// <param name="settings">The settings now in force.</param>
    /// <param name="launchLanguage">The language Clipo started with; a different choice needs a restart.</param>
    /// <param name="login">Whether Clipo starts with Windows; kept by Windows, not in the settings file.</param>
    /// <param name="extras">Snippets and export / import on the open history.</param>
    public SettingsWindow(AppSettings settings, AppLanguage launchLanguage, LoginItem login, Extras extras)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(login);
        ArgumentNullException.ThrowIfNull(extras);
        InitializeComponent();
        _settings = settings;
        _launchLanguage = launchLanguage;
        _login = login;
        _extras = extras;

        Title = s_strings.GetString("SettingsTitle");
        Root.FlowDirection = Appearance.Direction;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Clipo.ico"));
        AppWindow.Closing += OnClosing;
        WindowSize.Resize(AppWindow, s_size);

        foreach (var choice in Enum.GetValues<HotkeyChoice>())
        {
            HotkeyBox.Items.Add(new ComboBoxItem { Content = HotkeyChoices.Chord(choice).Display });
        }
        Reflect(settings);
    }

    /// <summary>Raised with the new settings each time the user changes one.</summary>
    public event EventHandler<AppSettings>? Changed;

    /// <summary>Raised after a snippet was added, changed or deleted.</summary>
    public event EventHandler? SnippetsChanged;

    /// <summary>Raised after an import added items or snippets.</summary>
    public event EventHandler? Imported;

    /// <summary>Raised when the user picks Restart after changing the language.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Brings the window to the front, showing it if it was hidden.</summary>
    public void ShowWindow()
    {
        ShowLogin();
        _ = LoadSnippetsAsync();
        AppWindow.Show();
        Activate();
    }

    /// <summary>Gives the window the theme the user chose.</summary>
    public void ApplyTheme(AppTheme theme) => Appearance.ApplyTheme(Root, theme);

    /// <summary>Closes the window for good, when the app quits.</summary>
    public void Quit()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// Shows <paramref name="settings"/> in the controls, whether they came
    /// from here or from elsewhere, such as the tray menu.
    /// </summary>
    /// <param name="settings">The settings now in force.</param>
    /// <param name="hotkeyRefused">Whether to say that the shortcut the user chose is used by another app.</param>
    public void Reflect(AppSettings settings, bool hotkeyRefused = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            _settings = settings;
            ThemeBox.SelectedIndex = (int)settings.Theme;
            LanguageBox.SelectedIndex = (int)settings.Language;
            HotkeyBox.SelectedIndex = (int)settings.Hotkey;
            AutoPasteBox.IsChecked = settings.AutoPaste;
            ShowMaxItems(settings.MaxItems);
            AutoClearBox.SelectedIndex = AppSettings.AutoClearDaysChoices.ToList().IndexOf(settings.AutoClearDays);
            PrivateModeBox.IsChecked = settings.PrivateMode;
            IgnoredList.ItemsSource = settings.IgnoredApps.ToList();
            RemoveAppButton.IsEnabled = false;
            HotkeyRefusedNote.Visibility = hotkeyRefused ? Visibility.Visible : Visibility.Collapsed;
            RestartPanel.Visibility = settings.Language != _launchLanguage ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _loading = false;
        }
        ShowLogin();
    }

    /// <summary>Lists the usual numbers of items, plus the saved one if a hand-edited file holds another.</summary>
    private void ShowMaxItems(int selected)
    {
        List<int> counts = [.. AppSettings.MaxItemsChoices];
        if (!counts.Contains(selected))
        {
            counts.Add(selected);
        }
        MaxItemsBox.Items.Clear();
        foreach (var count in counts.Order())
        {
            MaxItemsBox.Items.Add(new ComboBoxItem { Content = count.ToString("N0", CultureInfo.CurrentCulture), Tag = count });
        }
        MaxItemsBox.SelectedIndex = MaxItemsBox.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => (int)item.Tag == selected);
    }

    /// <summary>Shows whether Clipo starts with Windows; Windows may have changed it since, for example in Task Manager.</summary>
    private void ShowLogin()
    {
        StartAtLoginBox.IsChecked = _login.IsOn;
        LoginDisabledPanel.Visibility = _login.NeedsApproval ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Change(AppSettings next)
    {
        if (_loading || next == _settings)
        {
            return;
        }
        _settings = next;
        RestartPanel.Visibility = next.Language != _launchLanguage ? Visibility.Visible : Visibility.Collapsed;
        Changed?.Invoke(this, next);
    }

    private void AddApp(string? text)
    {
        var apps = IgnoredAppList.Add(_settings.IgnoredApps, text);
        if (apps.Count == _settings.IgnoredApps.Count)
        {
            return;
        }
        AppNameBox.Text = "";
        Change(_settings with { IgnoredApps = apps });
        IgnoredList.ItemsSource = apps.ToList();
        RemoveAppButton.IsEnabled = false;
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // The event can come while the page is still being built.
        if (GeneralPanel is null || HistoryPanel is null || PrivacyPanel is null || SnippetsPanel is null || DataPanel is null)
        {
            return;
        }
        var index = sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : 0;
        GeneralPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        SnippetsPanel.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        DataPanel.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Lists the saved snippets; says so if the history is not open.</summary>
    private async Task LoadSnippetsAsync()
    {
        var found = await _extras.SnippetsAsync();
        _snippets = found is null ? [] : [.. found];
        SnippetList.ItemsSource = _snippets.Select(snippet => snippet.Title).ToList();
        SnippetsEmptyText.Text = found is null ? s_strings.GetString("SnippetsUnavailable") : _snippets.Count == 0 ? s_strings.GetString("SnippetsNone") : "";
        SnippetsEmptyText.Visibility = SnippetsEmptyText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        SnippetList.Visibility = _snippets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var kept = _snippets.FindIndex(snippet => snippet.Id == _editingSnippet);
        if (kept >= 0)
        {
            SnippetList.SelectedIndex = kept;
        }
        else
        {
            EditSnippet(null);
        }
    }

    /// <summary>Fills the fields with <paramref name="snippet"/>, or empties them to write a new one.</summary>
    private void EditSnippet(Snippet? snippet)
    {
        _editingSnippet = snippet?.Id;
        SnippetTitleBox.Text = snippet?.Title ?? "";
        SnippetBodyBox.Text = snippet?.Body ?? "";
        DeleteSnippetButton.IsEnabled = snippet is not null;
        if (snippet is null)
        {
            SnippetList.SelectedIndex = -1;
        }
    }

    private void OnSnippetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedIndex is >= 0 and var index && index < _snippets.Count)
        {
            SnippetStatus.Text = "";
            EditSnippet(_snippets[index]);
        }
    }

    private void OnNewSnippet(object sender, RoutedEventArgs e)
    {
        SnippetStatus.Text = "";
        EditSnippet(null);
        SnippetTitleBox.Focus(FocusState.Programmatic);
    }

    private async void OnSaveSnippet(object sender, RoutedEventArgs e)
    {
        var title = SnippetTitleBox.Text.Trim();
        var body = SnippetBodyBox.Text;
        if (title.Length == 0 || string.IsNullOrWhiteSpace(body))
        {
            SnippetStatus.Text = s_strings.GetString("SnippetInvalid");
            return;
        }
        SaveSnippetButton.IsEnabled = false;
        try
        {
            if (await _extras.SaveSnippetAsync(_editingSnippet, title, body))
            {
                if (_editingSnippet is null)
                {
                    EditSnippet(null);
                }
                await LoadSnippetsAsync();
                // After the reload, which clears the message.
                SnippetStatus.Text = s_strings.GetString("SnippetSaved");
                SnippetsChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SnippetStatus.Text = s_strings.GetString("SnippetFailed");
            }
        }
        finally
        {
            SaveSnippetButton.IsEnabled = true;
        }
    }

    private async void OnDeleteSnippet(object sender, RoutedEventArgs e)
    {
        if (_editingSnippet is not { } id)
        {
            return;
        }
        if (await _extras.DeleteSnippetAsync(id))
        {
            SnippetStatus.Text = "";
            _editingSnippet = null;
            await LoadSnippetsAsync();
            SnippetsChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            SnippetStatus.Text = s_strings.GetString("SnippetFailed");
        }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            // The file holds clipboard content in the clear: say so before it is written.
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = s_strings.GetString("ExportConfirmTitle"),
                Content = s_strings.GetString("ExportConfirmText"),
                PrimaryButtonText = s_strings.GetString("ExportConfirmButton"),
                CloseButtonText = s_strings.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
            var picker = new FileSavePicker { SuggestedFileName = s_strings.GetString("ExportFileName") };
            picker.FileTypeChoices.Add(s_strings.GetString("ExportFileType"), [ExportExtension]);
            InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            if (await picker.PickSaveFileAsync() is not { } file)
            {
                return;
            }
            var data = await _extras.ExportAsync();
            if (data is null)
            {
                DataStatus.Text = s_strings.GetString("ExportFailed");
                return;
            }
            await FileIO.WriteBytesAsync(file, data);
            DataStatus.Text = s_strings.GetString("ExportDone");
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"writing the export failed: {ex.GetType().Name}");
            DataStatus.Text = s_strings.GetString("FileNotWritten");
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            picker.FileTypeFilter.Add(ExportExtension);
            if (await picker.PickSingleFileAsync() is not { } file)
            {
                return;
            }
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > MaxImportBytes)
            {
                DataStatus.Text = s_strings.GetString("ImportInvalid");
                return;
            }
            var buffer = await FileIO.ReadBufferAsync(file);
            var data = new byte[buffer.Length];
            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(data);
            }

            var result = await _extras.ImportAsync(data);
            DataStatus.Text = result.Outcome switch
            {
                ImportOutcome.Imported => ImportMessage(result.Summary!),
                ImportOutcome.Invalid => s_strings.GetString("ImportInvalid"),
                ImportOutcome.TooNew => s_strings.GetString("ImportTooNew"),
                _ => s_strings.GetString("ImportFailed"),
            };
            if (result.Outcome == ImportOutcome.Imported)
            {
                await LoadSnippetsAsync();
                Imported?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"reading the export failed: {ex.GetType().Name}");
            DataStatus.Text = s_strings.GetString("FileNotRead");
        }
    }

    private static string ImportMessage(ImportSummary summary) => string.Format(
        CultureInfo.CurrentCulture,
        s_strings.GetString("ImportDone"),
        summary.ItemsAdded,
        summary.SnippetsAdded,
        summary.ItemsDuplicate + summary.SnippetsDuplicate,
        summary.ItemsTooLarge);

    private void OnStartAtLoginClick(object sender, RoutedEventArgs e)
    {
        var worked = _login.SetOn(StartAtLoginBox.IsChecked == true);
        LoginFailedNote.Visibility = worked ? Visibility.Collapsed : Visibility.Visible;
        ShowLogin();
    }

    private void OnOpenStartupSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            Trace.TraceWarning($"opening the startup settings failed: {ex.GetType().Name}");
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedIndex >= 0)
        {
            Change(_settings with { Theme = Enum.GetValues<AppTheme>()[ThemeBox.SelectedIndex] });
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageBox.SelectedIndex >= 0)
        {
            Change(_settings with { Language = Enum.GetValues<AppLanguage>()[LanguageBox.SelectedIndex] });
        }
    }

    private void OnRestart(object sender, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);

    private void OnHotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotkeyBox.SelectedIndex >= 0 && !_loading)
        {
            HotkeyRefusedNote.Visibility = Visibility.Collapsed;
            Change(_settings with { Hotkey = Enum.GetValues<HotkeyChoice>()[HotkeyBox.SelectedIndex] });
        }
    }

    private void OnAutoPasteClick(object sender, RoutedEventArgs e) =>
        Change(_settings with { AutoPaste = AutoPasteBox.IsChecked == true });

    private void OnMaxItemsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaxItemsBox.SelectedItem is ComboBoxItem { Tag: int count })
        {
            Change(_settings with { MaxItems = count });
        }
    }

    private void OnAutoClearChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoClearBox.SelectedIndex >= 0)
        {
            Change(_settings with { AutoClearDays = AppSettings.AutoClearDaysChoices[AutoClearBox.SelectedIndex] });
        }
    }

    private void OnPrivateModeClick(object sender, RoutedEventArgs e) =>
        Change(_settings with { PrivateMode = PrivateModeBox.IsChecked == true });

    private void OnIgnoredSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveAppButton.IsEnabled = IgnoredList.SelectedItem is string;

    private void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (IgnoredList.SelectedItem is not string app)
        {
            return;
        }
        var apps = IgnoredAppList.Remove(_settings.IgnoredApps, app);
        Change(_settings with { IgnoredApps = apps });
        IgnoredList.ItemsSource = apps.ToList();
        RemoveAppButton.IsEnabled = false;
    }

    private void OnAddApp(object sender, RoutedEventArgs e) => AddApp(AppNameBox.Text);

    private void OnAppNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            AddApp(AppNameBox.Text);
        }
    }

    private async void OnChooseApp(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            picker.FileTypeFilter.Add(".exe");
            if (await picker.PickSingleFileAsync() is { } file)
            {
                AddApp(file.Name);
            }
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"choosing an app failed: {ex.GetType().Name}");
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
}
