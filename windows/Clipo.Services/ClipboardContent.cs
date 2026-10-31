using Clipo.Core;

namespace Clipo.Services;

/// <summary>
/// What one clipboard change holds, as raw data copied off the clipboard.
/// A change holds one kind of content, picked in the order files, text,
/// image: a spreadsheet range comes with a picture, and some apps add a
/// file's name as text.
/// </summary>
public abstract record ClipboardContent
{
    private ClipboardContent()
    {
    }

    /// <summary>Files copied in Explorer or another file manager.</summary>
    public sealed record Files(IReadOnlyList<string> Paths) : ClipboardContent;

    /// <summary>Text, with the rich forms the source also supplied: RTF and the HTML fragment.</summary>
    public sealed record Text(string Plain, string? Rtf = null, string? Html = null) : ClipboardContent;

    /// <summary>An image: a PNG, or a device-independent bitmap (<c>CF_DIB</c>) without a file header.</summary>
    public sealed record Image(byte[] Data, bool IsPng) : ClipboardContent;
}

/// <summary>One clipboard change, ready to be turned into a capture off the UI thread.</summary>
public sealed record ClipboardChange(ClipboardContent Content, SourceApp? SourceApp, DateTime Date);
