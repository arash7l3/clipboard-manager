using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>What a block of emoji in the grid is.</summary>
public enum EmojiSectionKind
{
    /// <summary>Emoji the user picked lately.</summary>
    Recent,

    /// <summary>One of the picker's groups.</summary>
    Group,

    /// <summary>What a search found.</summary>
    Results,
}

/// <summary>A block of emoji in the grid, under one heading.</summary>
/// <param name="Kind">What the block is.</param>
/// <param name="Group">Its group, for <see cref="EmojiSectionKind.Group"/>.</param>
/// <param name="Emoji">Its emoji.</param>
public sealed record EmojiGridSection(EmojiSectionKind Kind, EmojiGroup Group, IReadOnlyList<Emoji> Emoji);

/// <summary>An emoji's place in the grid.</summary>
public readonly record struct EmojiPosition(int Section, int Item);

/// <summary>How the selection moves in the emoji grid.</summary>
public enum EmojiMove
{
    Left,
    Right,
    RowUp,
    RowDown,
    PageUp,
    PageDown,
    First,
    Last,
}

/// <summary>
/// What the Emoji tab shows: the sections, the search text and which emoji is
/// selected. The sections are the recent emoji and the picker's groups, or,
/// while searching, one block of results. Moves follow the grid: each section
/// starts on a new row, <see cref="Columns"/> emoji to a row. Use it from the
/// UI thread.
/// </summary>
public sealed class EmojiModel
{
    /// <summary>The groups Windows cannot draw: Segoe UI Emoji shows a country flag as two letters.</summary>
    public static readonly IReadOnlySet<EmojiGroup> WindowsHidden = new HashSet<EmojiGroup> { EmojiGroup.Flags };

    private readonly List<EmojiSection> _catalog;
    private readonly Func<string, IReadOnlyList<Emoji>> _search;
    private readonly Func<IReadOnlyList<Emoji>> _loadRecent;
    private readonly IReadOnlySet<EmojiGroup> _hidden;
    private IReadOnlyList<Emoji> _recent = [];
    private int _generation;
    private int _columns = 9;
    private int _pageRows = 5;

    /// <param name="catalog">The picker's sections, in order.</param>
    /// <param name="search">Finds the emoji for the words typed.</param>
    /// <param name="loadRecent">Fetches the recently used emoji, most recent first. Called off the UI thread.</param>
    /// <param name="hidden">Groups left out, such as the flags Windows does not draw.</param>
    public EmojiModel(
        IReadOnlyList<EmojiSection> catalog,
        Func<string, IReadOnlyList<Emoji>> search,
        Func<IReadOnlyList<Emoji>> loadRecent,
        IReadOnlySet<EmojiGroup> hidden)
    {
        _hidden = hidden;
        _search = search;
        _loadRecent = loadRecent;
        _catalog = [.. catalog.Where(section => !hidden.Contains(section.Group))];
        Rebuild();
    }

    /// <summary>A model over the core's emoji list, without the flags.</summary>
    public EmojiModel(Func<IReadOnlyList<Emoji>> loadRecent)
        : this([.. ClipoCore.EmojiSections()], text => ClipoCore.SearchEmoji(text), loadRecent, WindowsHidden)
    {
    }

    /// <summary>Raised after <see cref="Sections"/> changed, and the selection with them.</summary>
    public event EventHandler? SectionsChanged;

    /// <summary>Raised after the selection moved within the same sections.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The groups the picker offers, in order.</summary>
    public IEnumerable<EmojiGroup> Groups => _catalog.Select(section => section.Group);

    public IReadOnlyList<EmojiGridSection> Sections { get; private set; } = [];

    /// <summary>The selected emoji's section and place in it; <c>null</c> only when there are no emoji.</summary>
    public EmojiPosition? Selection { get; private set; }

    public string SearchText { get; private set; } = "";

    /// <summary>Emoji to a row; the view sets it from its width.</summary>
    public int Columns
    {
        get => _columns;
        set => _columns = Math.Max(1, value);
    }

    /// <summary>Rows a page move goes; the view sets it from its height.</summary>
    public int PageRows
    {
        get => _pageRows;
        set => _pageRows = Math.Max(1, value);
    }

    public Emoji? SelectedEmoji => Selection is { } position ? At(position) : null;

    /// <summary>The emoji at <paramref name="position"/>, or <c>null</c> if there is none.</summary>
    public Emoji? At(EmojiPosition position) =>
        position.Section >= 0 && position.Section < Sections.Count
            && position.Item >= 0 && position.Item < Sections[position.Section].Emoji.Count
            ? Sections[position.Section].Emoji[position.Item]
            : null;

    /// <summary>The index of the section for <paramref name="group"/>, if the grid has one.</summary>
    public int? IndexOf(EmojiGroup group)
    {
        var index = Sections.ToList().FindIndex(section => section.Kind == EmojiSectionKind.Group && section.Group == group);
        return index >= 0 ? index : null;
    }

    /// <summary>The index of the Recent section, if the grid has one.</summary>
    public int? IndexOfRecent()
    {
        var index = Sections.ToList().FindIndex(section => section.Kind == EmojiSectionKind.Recent);
        return index >= 0 ? index : null;
    }

    /// <summary>Searches the emoji for <paramref name="text"/>, or lists the groups again if it is blank.</summary>
    public void SetSearchText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == SearchText)
        {
            return;
        }
        SearchText = text;
        Rebuild();
        SectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads the recent emoji again, for example when the popup opens. They are listed first while there is no search.</summary>
    public async Task ReloadRecentAsync()
    {
        var current = ++_generation;
        var found = await Task.Run(LoadRecent).ConfigureAwait(true);
        if (current != _generation)
        {
            return;
        }
        _recent = found;
        Rebuild();
        SectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects <paramref name="position"/>. Returns whether the selection changed.</summary>
    public bool Select(EmojiPosition position)
    {
        if (At(position) is null || Selection == position)
        {
            return false;
        }
        Selection = position;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Moves the selection, stopping at the first and last emoji. Returns whether it moved.</summary>
    public bool Move(EmojiMove move)
    {
        if (Selection is not { } current)
        {
            return false;
        }
        EmojiPosition? target = move switch
        {
            EmojiMove.Left => Step(current, -1),
            EmojiMove.Right => Step(current, 1),
            EmojiMove.RowUp => Rows(current, -1),
            EmojiMove.RowDown => Rows(current, 1),
            EmojiMove.PageUp => Rows(current, -PageRows),
            EmojiMove.PageDown => Rows(current, PageRows),
            EmojiMove.First => new EmojiPosition(0, 0),
            _ => Sections.Count > 0 ? new EmojiPosition(Sections.Count - 1, Sections[^1].Emoji.Count - 1) : null,
        };
        return target is { } position && Select(position);
    }

    private IReadOnlyList<Emoji> LoadRecent()
    {
        try
        {
            return [.. _loadRecent().Where(emoji => !_hidden.Contains(emoji.Group))];
        }
        catch (Exception e)
        {
            // Core errors carry codes, never clipboard content.
            Trace.TraceError($"listing the recent emoji failed: {e.GetType().Name}");
            return [];
        }
    }

    private void Rebuild()
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
        {
            List<EmojiGridSection> built = [];
            if (_recent.Count > 0)
            {
                built.Add(new(EmojiSectionKind.Recent, default, _recent));
            }
            built.AddRange(_catalog.Select(section => new EmojiGridSection(EmojiSectionKind.Group, section.Group, section.Emoji)));
            Sections = built;
        }
        else
        {
            var found = _search(text).Where(emoji => !_hidden.Contains(emoji.Group)).ToList();
            Sections = found.Count == 0 ? [] : [new(EmojiSectionKind.Results, default, found)];
        }
        Selection = Sections.Count == 0 ? null : new EmojiPosition(0, 0);
    }

    private EmojiPosition? Step(EmojiPosition start, int offset)
    {
        var total = Sections.Sum(section => section.Emoji.Count);
        var remaining = Math.Clamp(FlatIndex(start) + offset, 0, total - 1);
        for (var index = 0; index < Sections.Count; index++)
        {
            if (remaining < Sections[index].Emoji.Count)
            {
                return new EmojiPosition(index, remaining);
            }
            remaining -= Sections[index].Emoji.Count;
        }
        return null;
    }

    private int FlatIndex(EmojiPosition position) =>
        Sections.Take(position.Section).Sum(section => section.Emoji.Count) + position.Item;

    private EmojiPosition Rows(EmojiPosition start, int rows)
    {
        var current = start;
        for (var i = 0; i < Math.Abs(rows); i++)
        {
            if ((rows < 0 ? RowAbove(current) : RowBelow(current)) is not { } next)
            {
                break;
            }
            current = next;
        }
        return current;
    }

    /// <summary>The emoji below <paramref name="position"/> in the same column; in a short last row, the last emoji; past the last row, the next section's first row.</summary>
    private EmojiPosition? RowBelow(EmojiPosition position)
    {
        var count = Sections[position.Section].Emoji.Count;
        if (position.Item / Columns < (count - 1) / Columns)
        {
            return position with { Item = Math.Min(position.Item + Columns, count - 1) };
        }
        if (position.Section + 1 >= Sections.Count)
        {
            return null;
        }
        var next = Sections[position.Section + 1].Emoji.Count;
        return new EmojiPosition(position.Section + 1, Math.Min(position.Item % Columns, next - 1));
    }

    /// <summary>The emoji above <paramref name="position"/> in the same column; above the first row, the previous section's last row.</summary>
    private EmojiPosition? RowAbove(EmojiPosition position)
    {
        if (position.Item >= Columns)
        {
            return position with { Item = position.Item - Columns };
        }
        if (position.Section == 0)
        {
            return null;
        }
        var count = Sections[position.Section - 1].Emoji.Count;
        var lastRowStart = (count - 1) / Columns * Columns;
        return new EmojiPosition(position.Section - 1, Math.Min(lastRowStart + (position.Item % Columns), count - 1));
    }
}
