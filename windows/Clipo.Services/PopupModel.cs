using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>
/// What the popup shows: the tab, the search text, the rows and which row is
/// selected. Loads rows from the core off the UI thread and keeps only the
/// answer to the latest question. Use it from the UI thread.
/// </summary>
/// <param name="load">Fetches previews for a query; <c>null</c> if the store failed. Called off the UI thread.</param>
/// <param name="searchDelay">How long typing has to pause before the search runs.</param>
public sealed class PopupModel(Func<Query, ItemPreview[]?> load, TimeSpan searchDelay)
{
    /// <summary>The usual pause before a search runs.</summary>
    public static readonly TimeSpan DefaultSearchDelay = TimeSpan.FromMilliseconds(80);

    /// <summary>How many rows have a quick-paste shortcut: Ctrl+1 to Ctrl+9.</summary>
    public const int QuickPasteCount = 9;

    private int _generation;

    public PopupModel(Func<Query, ItemPreview[]?> load)
        : this(load, DefaultSearchDelay)
    {
    }

    /// <summary>Raised after <see cref="Rows"/> changed, and the selection with them.</summary>
    public event EventHandler? RowsChanged;

    /// <summary>Raised after the selection moved within the same rows.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The tabs, in display order.</summary>
    public static IReadOnlyList<Category> Categories { get; } = [Category.All, Category.Text, Category.Images, Category.Files, Category.Pinned];

    public Category Category { get; private set; } = Category.All;

    public string SearchText { get; private set; } = "";

    public IReadOnlyList<ItemPreview> Rows { get; private set; } = [];

    /// <summary>Index into <see cref="Rows"/>; <c>null</c> only when there are no rows.</summary>
    public int? Selection { get; private set; }

    /// <summary>The last load failed; <see cref="Rows"/> is empty.</summary>
    public bool Failed { get; private set; }

    /// <summary>A load is under way; <see cref="Rows"/> answer an earlier question.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>The current tab has a Clear button: every tab but Pinned, whose items clearing never touches.</summary>
    public bool CanClear => Category != Category.Pinned;

    public ItemPreview? SelectedRow => Selection is { } index ? Rows[index] : null;

    /// <summary>The row Ctrl+<paramref name="number"/> picks (1 is the first row), or <c>null</c> if the number has no row.</summary>
    public ItemPreview? RowForShortcut(int number) =>
        number >= 1 && number <= QuickPasteCount && number <= Rows.Count ? Rows[number - 1] : null;

    /// <summary>
    /// Back to the <paramref name="start"/> tab with no search and no rows
    /// until they load, for a freshly opened popup: Enter must not pick a row
    /// left from last time.
    /// </summary>
    public Task ResetAsync(Category start = Category.All)
    {
        Category = start;
        SearchText = "";
        // Cleared before the load starts: a quick answer must not be wiped by this.
        Rows = [];
        Selection = null;
        Failed = false;
        IsLoading = true;
        RowsChanged?.Invoke(this, EventArgs.Empty);
        return ReloadAsync(keepSelection: false, TimeSpan.Zero);
    }

    public Task SetCategoryAsync(Category category)
    {
        Category = category;
        return ReloadAsync(keepSelection: false, TimeSpan.Zero);
    }

    /// <summary>Shows the tab <paramref name="offset"/> places after the current one, wrapping around.</summary>
    public Task MoveCategoryAsync(int offset)
    {
        var count = Categories.Count;
        var index = Math.Max(0, Categories.ToList().IndexOf(Category));
        return SetCategoryAsync(Categories[(((index + offset) % count) + count) % count]);
    }

    public Task SetSearchTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == SearchText)
        {
            return Task.CompletedTask;
        }
        SearchText = text;
        return ReloadAsync(keepSelection: false, searchDelay);
    }

    /// <summary>Loads the rows again, for example after a new copy. The selected item stays selected if it is still listed.</summary>
    public Task RefreshAsync() => ReloadAsync(keepSelection: true, TimeSpan.Zero);

    /// <summary>Moves the selection <paramref name="offset"/> rows down (up if negative), stopping at the first and last row. Returns whether it moved.</summary>
    public bool MoveSelection(int offset) =>
        Selection is { } selection && Select(Math.Clamp(selection + offset, 0, Rows.Count - 1));

    /// <summary>Selects the row at <paramref name="index"/>. Returns whether the selection changed.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= Rows.Count || index == Selection)
        {
            return false;
        }
        Selection = index;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private async Task ReloadAsync(bool keepSelection, TimeSpan delay)
    {
        IsLoading = true;
        var current = ++_generation;
        var query = new Query(Category, SearchText);
        var keep = keepSelection ? SelectedRow?.Id : null;

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(true);
            // Typing went on: a later call asks the newer question.
            if (current != _generation)
            {
                return;
            }
        }
        var found = await Task.Run(() => Load(query)).ConfigureAwait(true);
        // A newer question may have been asked while this one ran.
        if (current == _generation)
        {
            Apply(found, keep);
        }
    }

    private ItemPreview[]? Load(Query query)
    {
        try
        {
            return load(query);
        }
        catch (Exception e)
        {
            Trace.TraceError($"listing the history failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private void Apply(ItemPreview[]? found, long? keep)
    {
        IsLoading = false;
        Failed = found is null;
        Rows = found ?? [];
        var kept = keep is { } id ? Array.FindIndex(found ?? [], row => row.Id == id) : -1;
        Selection = kept >= 0 ? kept : Rows.Count > 0 ? 0 : null;
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }
}
