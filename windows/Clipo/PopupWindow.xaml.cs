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
using Windows.System;

namespace Clipo;

/// <summary>
/// The history popup. It opens at the mouse pointer and takes the keyboard;
/// the search field keeps it the whole time. Typing searches; Up, Down,
/// Page Up, Page Down, Home and End move the selection; Tab and Shift+Tab
/// switch tabs; Enter picks the selected item; Esc closes. Ctrl+1 to Ctrl+9
/// pick the first nine rows. Holding Shift while picking, with Enter or a
/// click, pastes without formatting. Ctrl+P, or Pin in a row's right-click
/// menu, pins or unpins an item. The button beside the tabs clears the tab
/// after a second click. A click on a row picks it, and clicking anywhere
/// else closes the popup.
/// </summary>
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

    /// <summary>The window that was in front before the popup opened; it gets the focus back.</summary>
    private nint _previous;
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
        Root.Loaded += (_, _) =>
        {
            // The button's state is set again once the content is laid out: a popup that
            // was created at sign-in and never shown came up without it.
            ShowClearButton();
            SearchBox.Focus(FocusState.Programmatic);
        };

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
        Activated += OnActivated;
    }

    /// <summary>Raised with the row the user picked, after the popup closed and gave the focus back.</summary>
    public event EventHandler<PopupChoice>? Chosen;

    /// <summary>Raised when the user pins or unpins a row; the popup stays open.</summary>
    public event EventHandler<PopupPin>? PinRequested;

    /// <summary>Raised with the tab to clear, after the user clicked Clear twice; the popup stays open.</summary>
    public event EventHandler<Category>? ClearRequested;

    public bool IsShown { get; private set; }

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

    /// <summary>Opens the popup at the mouse pointer on the chosen start tab with an empty search.</summary>
    public void Show()
    {
        if (IsShown)
        {
            return;
        }
        _previous = Desktop.ForegroundWindow();
        _ = _model.ResetAsync(StartCategory(_model.Category));
        SearchBox.Text = "";
        ShowTab();
        Place();
        IsShown = true;
        AppWindow.Show();
        Activate();
        // Windows lets the app that got the shortcut or the click take the front.
        Desktop.Activate(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        SearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Hides the popup. With <paramref name="restoreFocus"/>, the window
    /// that was in front before gets the focus back, as after Esc or a pick;
    /// not after a click elsewhere, which already chose a window.
    /// </summary>
    public void Dismiss(bool restoreFocus)
    {
        if (!IsShown)
        {
            return;
        }
        IsShown = false;
        DisarmClear();
        // While the popup is still in front, Windows allows handing the front over.
        if (restoreFocus)
        {
            Desktop.Activate(_previous);
        }
        AppWindow.Hide();
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

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        if (e.Key == VirtualKey.P && Desktop.IsControlDown())
        {
            if (_model.SelectedRow is { } selected)
            {
                RequestPin(selected);
            }
            return;
        }
        if (Desktop.IsControlDown() && QuickPasteNumber(e.Key) is { } number)
        {
            if (_model.RowForShortcut(number) is { } quick)
            {
                Choose(quick, plain: Desktop.IsShiftDown());
            }
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Down:
                _model.MoveSelection(1);
                break;
            case VirtualKey.Up:
                _model.MoveSelection(-1);
                break;
            case VirtualKey.PageDown:
                _model.MoveSelection(PageRows);
                break;
            case VirtualKey.PageUp:
                _model.MoveSelection(-PageRows);
                break;
            case VirtualKey.Home:
                _model.Select(0);
                break;
            case VirtualKey.End:
                _model.Select(_model.Rows.Count - 1);
                break;
            case VirtualKey.Tab:
                _ = _model.MoveCategoryAsync(Desktop.IsShiftDown() ? -1 : 1);
                ShowTab();
                break;
            case VirtualKey.Enter:
                if (_model.SelectedRow is { } row)
                {
                    Choose(row, plain: Desktop.IsShiftDown());
                }
                break;
            case VirtualKey.Escape:
                Dismiss(restoreFocus: true);
                break;
            default:
                e.Handled = false;
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
        var flyout = new MenuFlyout();
        flyout.Items.Add(item);
        // The menu takes the focus; the search field gets it back.
        flyout.Closed += (_, _) => SearchBox.Focus(FocusState.Programmatic);
        flyout.ShowAt(HistoryList, new FlyoutShowOptions { Position = e.GetPosition(HistoryList) });
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
        SearchBox.Focus(FocusState.Programmatic);
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

    /// <summary>1 to 9 for the digit keys, in the top row or on the number pad.</summary>
    private static int? QuickPasteNumber(VirtualKey key) => key switch
    {
        >= VirtualKey.Number1 and <= VirtualKey.Number9 => key - VirtualKey.Number0,
        >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => key - VirtualKey.NumberPad0,
        _ => null,
    };

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : -1;
        if (index >= 0 && index < PopupModel.Categories.Count && PopupModel.Categories[index] != _model.Category)
        {
            _ = _model.SetCategoryAsync(PopupModel.Categories[index]);
        }
        DisarmClear();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PopupRow row)
        {
            Choose(row.Preview, plain: Desktop.IsShiftDown());
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // A click elsewhere, or another app taking the front, closes the popup.
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Dismiss(restoreFocus: false);
        }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Alt+F4 hides the popup like Esc; only quitting closes it.
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
