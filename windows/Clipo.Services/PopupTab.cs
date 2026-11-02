using Clipo.Core;

namespace Clipo.Services;

/// <summary>A tab of the popup: a list of history items, or the emoji picker.</summary>
/// <param name="Category">The history the tab lists; <c>null</c> for the Emoji tab, which lists no history items.</param>
public readonly record struct PopupTab(Category? Category)
{
    /// <summary>The emoji picker.</summary>
    public static PopupTab Emoji { get; } = new(null);

    public bool IsEmoji => Category is null;

    /// <summary>The tab listing the items of <paramref name="category"/>.</summary>
    public static implicit operator PopupTab(Category category) => new(category);

    /// <summary>The tab listing the items of <paramref name="category"/>.</summary>
    public static PopupTab FromCategory(Category category) => new(category);
}
