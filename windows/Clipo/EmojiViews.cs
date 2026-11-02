using System.ComponentModel;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Xaml;

namespace Clipo;

/// <summary>One emoji in the grid; it shows a highlight while it is the selected one.</summary>
public sealed class EmojiCell(Emoji emoji, EmojiPosition position) : INotifyPropertyChanged
{
    private bool _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Emoji Emoji { get; } = emoji;

    public EmojiPosition Position { get; } = position;

    public string Text => Emoji.Text;

    /// <summary>What a screen reader says: the emoji's English name.</summary>
    public string Name => Emoji.Name;

    public Visibility HighlightVisibility => _selected ? Visibility.Visible : Visibility.Collapsed;

    public void SetSelected(bool selected)
    {
        if (_selected != selected)
        {
            _selected = selected;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightVisibility)));
        }
    }

    /// <summary>The cell's name for screen readers comes from here.</summary>
    public override string ToString() => Name;
}

/// <summary>A heading and the emoji under it.</summary>
public sealed class EmojiSectionView
{
    public EmojiSectionView(EmojiGridSection section, int index, PopupText text)
    {
        Section = section;
        Title = text.EmojiSectionTitle(section);
        Cells = [.. section.Emoji.Select((emoji, item) => new EmojiCell(emoji, new EmojiPosition(index, item)))];
    }

    public EmojiGridSection Section { get; }

    public string Title { get; }

    public IReadOnlyList<EmojiCell> Cells { get; }
}
