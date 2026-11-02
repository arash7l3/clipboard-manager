using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Clipo;

/// <summary>The Emoji tab of the popup: the grid, its bar of section buttons and its keys.</summary>
public sealed partial class PopupWindow
{
    /// <summary>The emoji on each section's button in the bar.</summary>
    private static readonly Dictionary<EmojiGroup, string> s_barEmoji = new()
    {
        [EmojiGroup.SmileysAndPeople] = "😀",
        [EmojiGroup.AnimalsAndNature] = "🐾",
        [EmojiGroup.FoodAndDrink] = "🍴",
        [EmojiGroup.Activities] = "⚽",
        [EmojiGroup.TravelAndPlaces] = "🚗",
        [EmojiGroup.Objects] = "💡",
        [EmojiGroup.Symbols] = "❤",
        [EmojiGroup.Flags] = "🏁",
    };

    private const string RecentBarEmoji = "🕒";

    /// <summary>Width of a cell, to work out how many fit in a row.</summary>
    private const double EmojiCellSize = 40;

    private EmojiModel? _emoji;
    private List<EmojiSectionView> _emojiSections = [];
    private EmojiCell? _selectedCell;

    /// <summary>Fetches the emoji the user picked lately, most recent first. Called off the UI thread.</summary>
    public Func<IReadOnlyList<Emoji>> RecentEmoji { get; set; } = () => [];

    /// <summary>Raised with the emoji the user picked, after the popup closed and gave the focus back.</summary>
    public event EventHandler<Emoji>? EmojiChosen;

    /// <summary>The emoji picker's model, made when the Emoji tab is first needed.</summary>
    private EmojiModel EmojiState
    {
        get
        {
            if (_emoji is null)
            {
                _emoji = new EmojiModel(() => RecentEmoji());
                _emoji.SectionsChanged += (_, _) => ShowEmojiSections();
                _emoji.SelectionChanged += (_, _) => ShowEmojiSelection(announce: true);
            }
            return _emoji;
        }
    }

    /// <summary>Shows the grid on the Emoji tab and the list on the others.</summary>
    private void ShowEmojiPanel()
    {
        var emoji = _model.IsEmojiTab;
        EmojiPanel.Visibility = emoji ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = emoji ? Visibility.Collapsed : Visibility.Visible;
        if (emoji)
        {
            EmojiState.SetSearchText(SearchBox.Text);
            ShowEmojiSections();
            ReloadRecentEmoji();
        }
        else
        {
            ShowRows();
        }
    }

    /// <summary>Loads the recent emoji for a freshly opened popup.</summary>
    private void ReloadRecentEmoji() => _ = EmojiState.ReloadRecentAsync();

    private void ShowEmojiSections()
    {
        if (!_model.IsEmojiTab)
        {
            return;
        }
        var emoji = EmojiState;
        _selectedCell = null;
        _emojiSections = [.. emoji.Sections.Select((section, index) => new EmojiSectionView(section, index, _text))];
        EmojiSections.ItemsSource = _emojiSections;
        EmojiScroller.ChangeView(null, 0, null, disableAnimation: true);
        BuildEmojiBar();
        ShowEmptyText();
        ShowEmojiSelection(announce: false);
    }

    private void BuildEmojiBar()
    {
        EmojiBar.Children.Clear();
        EmojiBar.Visibility = EmojiState.SearchText.Trim().Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        for (var index = 0; index < EmojiState.Sections.Count && EmojiBar.Visibility == Visibility.Visible; index++)
        {
            var section = EmojiState.Sections[index];
            var name = _text.EmojiSectionTitle(section);
            var target = index;
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = section.Kind == EmojiSectionKind.Recent ? RecentBarEmoji : s_barEmoji.GetValueOrDefault(section.Group, "🙂"),
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = 16,
                    IsTextScaleFactorEnabled = false,
                },
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 0,
                MinHeight = 0,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                IsTabStop = false,
                AllowFocusOnInteraction = false,
                Tag = index,
            };
            ToolTipService.SetToolTip(button, name);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) => JumpToEmojiSection(target);
            EmojiBar.Children.Add(button);
        }
        ShowEmojiBarSelection();
    }

    /// <summary>Tints the button of the section the selected emoji is in.</summary>
    private void ShowEmojiBarSelection()
    {
        var current = EmojiState.Selection?.Section;
        foreach (var child in EmojiBar.Children.OfType<Button>())
        {
            var selected = child.Tag is int index && index == current;
            child.Background = selected
                ? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void JumpToEmojiSection(int index)
    {
        EmojiState.Select(new EmojiPosition(index, 0));
        BringSectionToTop(index);
    }

    private void BringSectionToTop(int index)
    {
        EmojiScroller.UpdateLayout();
        var element = EmojiSections.TryGetElement(index) ?? EmojiSections.GetOrCreateElement(index);
        element.UpdateLayout();
        element.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
    }

    private void ShowEmojiSelection(bool announce)
    {
        if (!_model.IsEmojiTab)
        {
            return;
        }
        _selectedCell?.SetSelected(false);
        _selectedCell = null;
        if (EmojiState.Selection is { } position && position.Section < _emojiSections.Count)
        {
            var cell = _emojiSections[position.Section].Cells[position.Item];
            cell.SetSelected(true);
            _selectedCell = cell;
            BringCellIntoView(position);
            if (announce)
            {
                var peer = FrameworkElementAutomationPeer.FromElement(SearchBox) ?? FrameworkElementAutomationPeer.CreatePeerForElement(SearchBox);
                peer.RaiseNotificationEvent(
                    AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, cell.Name, "ClipoSelection");
            }
        }
        ShowEmojiBarSelection();
    }

    private void BringCellIntoView(EmojiPosition position)
    {
        try
        {
            EmojiScroller.UpdateLayout();
            var section = EmojiSections.TryGetElement(position.Section) ?? EmojiSections.GetOrCreateElement(position.Section);
            if (FindRepeater(section) is { } cells)
            {
                section.UpdateLayout();
                var element = cells.TryGetElement(position.Item) ?? cells.GetOrCreateElement(position.Item);
                element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // The grid was rebuilt under us; the next move scrolls.
            System.Diagnostics.Trace.TraceWarning($"scrolling the emoji grid failed: {e.GetType().Name}");
        }
    }

    private static ItemsRepeater? FindRepeater(UIElement section) =>
        section is Panel panel ? panel.Children.OfType<ItemsRepeater>().FirstOrDefault() : null;

    /// <summary>The model's search text for the emoji, kept the same as the field's.</summary>
    private void SearchEmoji(string text) => EmojiState.SetSearchText(text);

    private void OnEmojiSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width - 16;
        if (_emoji is { } emoji && width > 0)
        {
            emoji.Columns = Math.Min(9, Math.Max(1, (int)(width / EmojiCellSize)));
            emoji.PageRows = Math.Max(1, (int)(e.NewSize.Height / EmojiCellSize) - 1);
        }
    }

    private void OnEmojiTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmojiCell cell)
        {
            ChooseEmoji(cell.Emoji);
        }
    }

    private void ChooseEmoji(Emoji emoji)
    {
        Dismiss(restoreFocus: true);
        EmojiChosen?.Invoke(this, emoji);
    }

    /// <summary>The popup's keys on the Emoji tab. Quick paste and pin do nothing here.</summary>
    private void OnEmojiKey(PopupKeyCommand command)
    {
        var emoji = EmojiState;
        switch (command.Action)
        {
            case PopupKeyAction.Left:
                emoji.Move(EmojiMove.Left);
                break;
            case PopupKeyAction.Right:
                emoji.Move(EmojiMove.Right);
                break;
            case PopupKeyAction.Up:
                emoji.Move(EmojiMove.RowUp);
                break;
            case PopupKeyAction.Down:
                emoji.Move(EmojiMove.RowDown);
                break;
            case PopupKeyAction.PageUp:
                emoji.Move(EmojiMove.PageUp);
                break;
            case PopupKeyAction.PageDown:
                emoji.Move(EmojiMove.PageDown);
                break;
            case PopupKeyAction.Home:
                emoji.Move(EmojiMove.First);
                break;
            case PopupKeyAction.End:
                emoji.Move(EmojiMove.Last);
                break;
            case PopupKeyAction.Pick when emoji.SelectedEmoji is { } selected:
                ChooseEmoji(selected);
                break;
        }
    }
}
