using Clipo.Core;

namespace Clipo.Services;

/// <summary>An image ready for the core: encoded, measured, with its thumbnail.</summary>
public sealed record ProcessedImage(uint Width, uint Height, ImageFormat Format, byte[] Data, byte[]? Thumbnail);

/// <summary>Decodes images, which needs Windows' imaging components.</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Measures <paramref name="encoded"/> and makes a PNG thumbnail no longer
    /// than <paramref name="thumbnailPixels"/> on a side. A PNG comes back as
    /// it is; any other format is re-encoded as PNG, which is far smaller than
    /// a bitmap. <c>null</c> if the image cannot be decoded or has more than
    /// <paramref name="maxPixels"/> pixels.
    /// </summary>
    Task<ProcessedImage?> ProcessAsync(byte[] encoded, bool isPng, long maxPixels, int thumbnailPixels);
}

/// <summary>Turns a clipboard change into a capture for the core.</summary>
public sealed class CaptureBuilder(IImageProcessor images)
{
    /// <summary>More files than this in one copy are not kept; the list would be huge and each file costs a file-system call.</summary>
    public const int MaxFiles = 1000;

    /// <summary>Images with more pixels than this are not kept; decoding them would take too much memory.</summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>Longest side of the thumbnail stored with an image, in pixels.</summary>
    public const int ThumbnailPixels = 256;

    /// <summary>
    /// The capture for <paramref name="change"/>, or <c>null</c> if it is not
    /// worth keeping or is larger than <paramref name="maxItemBytes"/> (the
    /// core would skip those anyway; this avoids decoding them first).
    /// </summary>
    public async Task<Capture?> BuildAsync(ClipboardChange change, long maxItemBytes)
    {
        ArgumentNullException.ThrowIfNull(change);
        ItemContent? content = change.Content switch
        {
            ClipboardContent.Files files => FilesContent(files),
            ClipboardContent.Text text => TextContent(text, maxItemBytes),
            ClipboardContent.Image image => await ImageContentAsync(image, maxItemBytes).ConfigureAwait(false),
            _ => null,
        };
        return content is null ? null : new Capture(content, change.Date, change.SourceApp);
    }

    private static ItemContent.Files? FilesContent(ClipboardContent.Files files)
    {
        var paths = files.Paths.Where(path => !string.IsNullOrEmpty(path)).ToArray();
        if (paths.Length == 0 || paths.Length > MaxFiles)
        {
            return null;
        }
        return new ItemContent.Files([.. paths.Select(path => new FileRef(path, SizeOf(path)))]);
    }

    /// <summary>The size of a file, or <c>null</c> for a folder or a file that is gone.</summary>
    private static ulong? SizeOf(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? (ulong)file.Length : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static ItemContent.Text? TextContent(ClipboardContent.Text text, long maxBytes)
    {
        // Whitespace alone is not worth a history entry.
        if (string.IsNullOrWhiteSpace(text.Plain))
        {
            return null;
        }
        RichText? rich = null;
        if (!string.IsNullOrEmpty(text.Rtf) && text.Rtf.Length <= maxBytes)
        {
            rich = new RichText(RichTextFormat.Rtf, text.Rtf);
        }
        else if (!string.IsNullOrEmpty(text.Html) && text.Html.Length <= maxBytes)
        {
            rich = new RichText(RichTextFormat.Html, text.Html);
        }
        return new ItemContent.Text(new TextContent(text.Plain, rich));
    }

    private async Task<ItemContent?> ImageContentAsync(ClipboardContent.Image image, long maxBytes)
    {
        var encoded = image.Data;
        if (image.IsPng)
        {
            if (encoded.LongLength > maxBytes)
            {
                return null;
            }
        }
        else if (Dib.ToBmpFile(encoded) is { } bmp)
        {
            encoded = bmp;
        }
        else
        {
            return null;
        }

        var processed = await images.ProcessAsync(encoded, image.IsPng, MaxPixels, ThumbnailPixels).ConfigureAwait(false);
        if (processed is null || processed.Data.LongLength > maxBytes)
        {
            return null;
        }
        return new ItemContent.Image(new ImageContent(processed.Format, processed.Width, processed.Height, processed.Data, processed.Thumbnail));
    }
}
