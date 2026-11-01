using System.ComponentModel;
using System.Diagnostics;
using Clipo.Core;
using Clipo.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Clipo;

/// <summary>
/// One history item as a popup row shows it: an icon or thumbnail, a title, a details line, a pin mark,
/// and buttons that rename and delete it. While it is renamed, the new name shows in place of its lines.
/// </summary>
public sealed class PopupRow : INotifyPropertyChanged
{
    private string? _renameText;
    private bool _renameFresh;

    private PopupRow(ItemPreview preview, string title, string detail, string accessibleName, string glyph, ImageSource? thumbnail, string shortcut, PopupText text)
    {
        Preview = preview;
        Title = title;
        Detail = detail;
        AccessibleName = accessibleName;
        Glyph = glyph;
        Thumbnail = thumbnail;
        Shortcut = shortcut;
        RenameLabel = text.RenameItem;
        DeleteLabel = text.DeleteItem;
        RenameHint = text.ItemNameHint;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ItemPreview Preview { get; }

    public string Title { get; }

    public string Detail { get; }

    /// <summary>Everything the row shows, in one sentence for screen readers.</summary>
    public string AccessibleName { get; }

    /// <summary>A Segoe Fluent Icons character for the kind of content.</summary>
    public string Glyph { get; }

    public ImageSource? Thumbnail { get; }

    /// <summary>The quick-paste key, such as "Ctrl+1"; empty past the ninth row.</summary>
    public string Shortcut { get; }

    public Visibility GlyphVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ThumbnailVisibility => Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility PinVisibility => Preview.Pinned ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The rename button's tooltip and name.</summary>
    public string RenameLabel { get; }

    /// <summary>The delete button's tooltip and name.</summary>
    public string DeleteLabel { get; }

    /// <summary>What the rename field shows while it is empty.</summary>
    public string RenameHint { get; }

    /// <summary>The new name typed so far while the row is renamed; <c>null</c> otherwise.</summary>
    public string? RenameText => _renameText;

    /// <summary><see cref="RenameText"/> for the rename field, which takes no <c>null</c>.</summary>
    public string RenameShown => _renameText ?? "";

    /// <summary>The old name is still whole; the field shows it selected.</summary>
    public bool RenameFresh => _renameFresh;

    public Visibility LinesVisibility => _renameText is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RenameVisibility => _renameText is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shows <paramref name="rename"/>'s name in place of the row's lines, or the lines again for <c>null</c>.</summary>
    public void ShowRename(PopupRename? rename)
    {
        _renameText = rename?.Text;
        _renameFresh = rename?.IsFresh ?? false;
        Changed(nameof(RenameText));
        Changed(nameof(RenameShown));
        Changed(nameof(RenameFresh));
        Changed(nameof(LinesVisibility));
        Changed(nameof(RenameVisibility));
    }

    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>The list item's name for screen readers comes from here.</summary>
    public override string ToString() => AccessibleName;

    /// <summary>
    /// A row for <paramref name="preview"/>, the <paramref name="position"/>th in the list (from 1), which
    /// the first nine rows show as their quick-paste key. Call it on the UI thread: it creates the thumbnail image.
    /// </summary>
    public static PopupRow Create(ItemPreview preview, PopupText text, DateTime now, int position)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(text);
        var thumbnail = preview.Content is PreviewContent.Image { Thumbnail: { Length: > 0 } png } ? LoadThumbnail(png) : null;
        return new PopupRow(
            preview,
            text.Title(preview),
            text.Detail(preview, now),
            text.AccessibleName(preview, now),
            GlyphFor(preview.Content),
            thumbnail,
            position <= PopupModel.QuickPasteCount ? text.Shortcut(position) : "",
            text);
    }

    private static string GlyphFor(PreviewContent content) => content switch
    {
        PreviewContent.Text { Hint: TextHint.Link } => "",
        PreviewContent.Text { Hint: TextHint.Code } => "",
        PreviewContent.Text => "",
        PreviewContent.Image => "",
        PreviewContent.Files => "",
        _ => "",
    };

    /// <summary>An image that fills in once the PNG is decoded.</summary>
    private static BitmapImage LoadThumbnail(byte[] png)
    {
        var image = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 32 };
        _ = LoadAsync(image, png);
        return image;
    }

    private static async Task LoadAsync(BitmapImage image, byte[] png)
    {
        try
        {
            using var stream = await Streams.FromBytesAsync(png);
            await image.SetSourceAsync(stream);
        }
        catch (Exception e)
        {
            // The row shows no picture; never log the image itself.
            Trace.TraceWarning($"showing a thumbnail failed: {e.GetType().Name}");
        }
    }
}
