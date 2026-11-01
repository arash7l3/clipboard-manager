using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace Clipo;

/// <summary>
/// The history popup. It opens at the mouse pointer without taking the focus
/// from the app the user is typing in: the window is never activated, and
/// its keys are caught on their way to that app (<see cref="PopupInput"/>).
/// Typing searches; Up, Down, Page Up, Page Down, Home and End move the
/// selection; Tab and Shift+Tab switch tabs; Enter picks the selected item;
/// Esc closes. Ctrl+1 to Ctrl+9 pick the first nine rows. Holding Shift while
/// picking, with Enter or a click, pastes without formatting. Ctrl+P, or Pin
/// in a row's right-click menu, pins or unpins an item. The button beside the
/// tabs clears the tab after a second click. A click on a row picks it;
/// clicking anywhere else, or another window coming to the front, closes the
/// popup.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "Lives as long as the app; the input thread is disposed in Quit.")]
public sealed partial class PopupWindow : Window
{
    /// <summary>The popup's size, in effective pixels.</summary>
    private static readonly SizeInt32 s_size = new(420, 520);

    /// <summary>Rows that Page Up and Page Down move by.</summary>
    private const int PageRows = 8;

    private static readonly ResourceLoader s_strings = new();

    private readonly PopupModel _model;
    private readonly PopupText _text = new(s_strings.GetString, CultureInfo.CurrentCulture);
    private readonly bool _rightToLeft = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
    private readonly ClearConfirmation _clear = new();
    private readonly DispatcherTimer _clearTimer = new();
    private readonly PopupInput _input = new();

    /// <summary>The window the user was working in when the popup opened; it keeps the focus, and the paste goes there.</summary>
    private nint _previous;

    /// <summary>The window in front when the popup opened, such as the taskbar after a click on the tray icon.</summary>
    private nint _openedOver;

    /// <summary>
    /// Asked for the window to give the focus back to when the popup opens.
    /// The app passes one that skips the taskbar, which is in front after a
    /// click on the tray icon.
    /// </summary>
    public Func<nint> WindowInFront { get; set; } = Desktop.ForegroundWindow;

    /// <summary>The shortcut that opens the popup; pressed while it is open, it closes it.</summary>
    public Func<HotkeyChord?> Hotkey { get; set; } = () => null;

    private bool _allowClose;

    public PopupWindow(PopupModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        _model = model;
        _model.RowsChanged += (_, _) => ShowRows();
        _model.SelectionChanged += (_, _) => ShowSelection(announce: true);

        _clearTimer.Interval = _clear.Window;
        _clearTimer.Tick += (_, _) => DisarmClear();
        ShowClearButton();

        Root.FlowDirection = _rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // The first time, the content loads after the window is shown.
        // The button's state is set again once the content is laid out: a popup that
        // was created at sign-in and never shown came up without it.
        Root.Loaded += (_, _) => ShowClearButton();

        // Raised on the input thread.
        _input.KeyPressed += (_, command) => DispatcherQueue.TryEnqueue(() => OnKey(command));
        _input.ClickedOutside += (_, _) => DispatcherQueue.TryEnqueue(() => Dismiss(restoreFocus: false));

        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        AppWindow.Closing += OnClosing;
        Desktop.MakeNonActivating(Handle);
    }

    /// <summary>Raised with the row the user picked, after the popup closed and gave the focus back.</summary>
    public event EventHandler<PopupChoice>? Chosen;

    /// <summary>Raised when the user pins or unpins a row; the popup stays open.</summary>
    public event EventHandler<PopupPin>? PinRequested;

    /// <summary>Raised with the tab to clear, after the user clicked Clear twice; the popup stays open.</summary>
    public event EventHandler<Category>? ClearRequested;

    public bool IsShown { get; private set; }

    /// <summary>The window a pick is pasted into: the one the user was working in when the popup opened.</summary>
    public nint PasteTarget => _previous;

    private nint Handle => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    /// <summary>
    /// The tab to open on, given the tab the popup was on when it was last
    /// open. All, until the app says otherwise.
    /// </summary>
    public Func<Category, Category> StartCategory { get; set; } = _ => Category.All;

    /// <summary>Shows a note at the top of the popup while private mode is on.</summary>
    public void ShowPrivateMode(bool on) => PrivateNote.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gives the popup the theme the user chose.</summary>
    public void ApplyTheme(AppTheme theme) => Appearance.ApplyTheme(Root, theme);

    /// <summary>Opens the popup, or closes it if it is open.</summary>
    public void Toggle()
    {
        if (IsShown)
        {
            Dismiss(restoreFocus: true);
        }
        else
        {
            Show();
        }
    }

    /// <summary>
    /// Opens the popup at the mouse pointer on the chosen start tab with an
    /// empty search. The window the user was working in keeps the focus.
    /// </summary>
    public void Show()
    {
        if (IsShown)
        {
            return;
        }
        _openedOver = Desktop.ForegroundWindow();
        _previous = WindowInFront();
        // After a click on the tray icon the taskbar is in front: the app gets the
        // front back while Windows still lets Clipo hand it over.
        if (_previous != 0 && _previous != _openedOver)
        {
            Desktop.Activate(_previous);
        }
        _ = _model.ResetAsync(StartCategory(_model.Category));
        SetSearch("");
        ShowTab();
        Place();
        IsShown = true;
        var window = Handle;
        Desktop.MakeNonActivating(window);
        AppWindow.Show(activateWindow: false);
        Desktop.BringToTop(window);
        _input.Start(Hotkey());
    }

    /// <summary>
    /// Hides the popup. The popup never takes the front, but if Windows
    /// gave it the front anyway, <paramref name="restoreFocus"/> hands it
    /// back to the window the user was working in, as after Esc or a pick;
    /// not after a click elsewhere, which already chose a window.
    /// </summary>
    public void Dismiss(bool restoreFocus)
    {
        if (!IsShown)
        {
            return;
        }
        IsShown = false;
        _input.Stop();
        DisarmClear();
        if (restoreFocus && Desktop.IsOwnWindow(Desktop.ForegroundWindow()))
        {
            Desktop.Activate(_previous);
        }
        AppWindow.Hide();
    }

    /// <summary>
    /// Told about each window that comes to the front. One other than the
    /// window the user was working in closes the popup, as Alt+Tab or the
    /// Start menu do; the taskbar the popup was opened from does not.
    /// </summary>
    public void ForegroundChanged(nint window)
    {
        if (IsShown && window != _previous && window != _openedOver && !Desktop.IsOwnWindow(window))
        {
            Dismiss(restoreFocus: false);
        }
    }

    /// <summary>Lists new copies while the popup is open.</summary>
    public void HistoryChanged()
    {
        if (IsShown)
        {
            _ = _model.RefreshAsync();
        }
    }

    /// <summary>Closes the window for good, when the app quits.</summary>
    public void Quit()
    {
        _input.Dispose();
        _allowClose = true;
        Close();
    }

    private void Choose(ItemPreview row, bool plain)
    {
        Dismiss(restoreFocus: true);
        Chosen?.Invoke(this, new PopupChoice(row, plain));
    }

    private void Place()
    {
        var screen = Desktop.PointerScreen();
        var scale = screen.Dpi / 96.0;
        var frame = PopupPlacement.Frame(
            (int)(s_size.Width * scale), (int)(s_size.Height * scale), screen.Cursor, screen.WorkArea, _rightToLeft);
        var rect = new RectInt32(frame.Left, frame.Top, frame.Width, frame.Height);
        // Twice: a move onto a monitor with another scale resizes the window on the way.
        AppWindow.MoveAndResize(rect);
        AppWindow.MoveAndResize(rect);
    }

    private void ShowRows()
    {
        var now = DateTime.UtcNow;
        var rows = _model.Rows.Select((row, position) => PopupRow.Create(row, _text, now, position + 1)).ToList();
        HistoryList.ItemsSource = rows;
        var empty = rows.Count == 0 && !_model.IsLoading;
        EmptyText.Text = empty ? _text.EmptyList(_model.Failed, _model.SearchText.Length > 0) : "";
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ShowSelection(announce: false);
    }

    private void ShowSelection(bool announce)
    {
        HistoryList.SelectedIndex = _model.Selection ?? -1;
        if (HistoryList.SelectedItem is not PopupRow row)
        {
            return;
        }
        HistoryList.ScrollIntoView(row);
        // The focus stays in the search field, so screen readers are told about the row.
        if (announce)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(SearchBox) ?? FrameworkElementAutomationPeer.CreatePeerForElement(SearchBox);
            peer.RaiseNotificationEvent(
                AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, row.AccessibleName, "ClipoSelection");
        }
    }

    /// <summary>Shows the model's tab as selected in the tab bar.</summary>
    private void ShowTab()
    {
        DisarmClear();
        var index = PopupModel.Categories.ToList().IndexOf(_model.Category);
        if (index >= 0 && index < Tabs.Items.Count)
        {
            Tabs.SelectedItem = Tabs.Items[index];
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _ = _model.SetSearchTextAsync(SearchBox.Text);

    /// <summary>The search field shows what was typed; it never has the focus.</summary>
    private void SetSearch(string text)
    {
        SearchBox.Text = text;
        SearchBox.SelectionStart = text.Length;
    }

    private void OnKey(PopupKeyCommand command)
    {
        if (!IsShown)
        {
            return;
        }
        switch (command.Action)
        {
            case PopupKeyAction.Down:
                _model.MoveSelection(1);
                break;
            case PopupKeyAction.Up:
                _model.MoveSelection(-1);
                break;
            case PopupKeyAction.PageDown:
                _model.MoveSelection(PageRows);
                break;
            case PopupKeyAction.PageUp:
                _model.MoveSelection(-PageRows);
                break;
            case PopupKeyAction.Home:
                _model.Select(0);
                break;
            case PopupKeyAction.End:
                _model.Select(_model.Rows.Count - 1);
                break;
            case PopupKeyAction.NextTab or PopupKeyAction.PreviousTab:
                _ = _model.MoveCategoryAsync(command.Action == PopupKeyAction.NextTab ? 1 : -1);
                ShowTab();
                break;
            case PopupKeyAction.Pick when _model.SelectedRow is { } row:
                Choose(row, command.Plain);
                break;
            case PopupKeyAction.QuickPaste when _model.RowForShortcut(command.Number) is { } quick:
                Choose(quick, command.Plain);
                break;
            case PopupKeyAction.Pin when _model.SelectedRow is { } selected:
                RequestPin(selected);
                break;
            case PopupKeyAction.Close:
                Dismiss(restoreFocus: true);
                break;
            case PopupKeyAction.Type:
                SetSearch(SearchBox.Text + command.Text);
                break;
            case PopupKeyAction.Backspace:
                SetSearch(PopupKeys.Backspace(SearchBox.Text));
                break;
            case PopupKeyAction.DeleteWord:
                SetSearch(PopupKeys.DeleteWord(SearchBox.Text));
                break;
        }
    }

    private void RequestPin(ItemPreview row) => PinRequested?.Invoke(this, new PopupPin(row, !row.Pinned));

    private void OnRowRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not PopupRow row)
        {
            return;
        }
        e.Handled = true;
        var index = _model.Rows.ToList().FindIndex(preview => preview.Id == row.Preview.Id);
        if (index >= 0)
        {
            _model.Select(index);
        }
        var item = new MenuFlyoutItem
        {
            Text = s_strings.GetString(row.Preview.Pinned ? "MenuUnpin" : "MenuPin"),
            KeyboardAcceleratorTextOverride = "Ctrl+P",
        };
        item.Click += (_, _) => RequestPin(row.Preview);
        // Drawn inside the popup and without taking the focus, so the app underneath keeps it.
        var flyout = new MenuFlyout { ShouldConstrainToRootBounds = true };
        flyout.Items.Add(item);
        flyout.ShowAt(HistoryList, new FlyoutShowOptions { Position = e.GetPosition(HistoryList), ShowMode = FlyoutShowMode.Transient });
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_clear.Press())
        {
            DisarmClear();
            ClearRequested?.Invoke(this, _model.Category);
        }
        else
        {
            _clearTimer.Start();
            ShowClearButton();
        }
    }

    /// <summary>Back to the plain Clear button, for the tab now shown.</summary>
    private void DisarmClear()
    {
        _clearTimer.Stop();
        _clear.Disarm();
        ShowClearButton();
    }

    private void ShowClearButton()
    {
        var armed = _clear.IsArmed;
        // Hidden by opacity, not collapsed: the button keeps its place in the layout in every state.
        ClearButton.Opacity = _model.CanClear ? 1 : 0;
        ClearButton.IsEnabled = _model.CanClear;
        ClearButton.IsHitTestVisible = _model.CanClear;
        ClearIcon.Visibility = armed ? Visibility.Collapsed : Visibility.Visible;
        ClearConfirm.Visibility = armed ? Visibility.Visible : Visibility.Collapsed;
        var name = armed ? s_strings.GetString("ClearConfirmName") : _text.ClearTab(_model.Category);
        ToolTipService.SetToolTip(ClearButton, name);
        AutomationProperties.SetName(ClearButton, name);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : -1;
        if (index >= 0 && index < PopupModel.Categories.Count && PopupModel.Categories[index] != _model.Category)
        {
            _ = _model.SetCategoryAsync(PopupModel.Categories[index]);
        }
        DisarmClear();
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PopupRow row)
        {
            Choose(row.Preview, plain: Desktop.IsShiftDown());
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4, should the popup ever have the front, hides it like Esc; only quitting closes it.
        if (!_allowClose)
        {
            args.Cancel = true;
            Dismiss(restoreFocus: true);
        }
    }
}

/// <summary>What the user picked in the popup.</summary>
/// <param name="Row">The item.</param>
/// <param name="Plain">Paste it as plain text, without formatting.</param>
public sealed record PopupChoice(ItemPreview Row, bool Plain);

/// <summary>A request to pin or unpin a row.</summary>
/// <param name="Row">The item.</param>
/// <param name="Pinned">Whether it should be pinned afterwards.</param>
public sealed record PopupPin(ItemPreview Row, bool Pinned);
