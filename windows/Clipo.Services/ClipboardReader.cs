using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>
/// Reads what is on the Windows clipboard. Concealed data is never read.
/// Blocks while another app holds the clipboard open, so call it off the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class ClipboardReader
{
    /// <summary>
    /// A bitmap larger than this, uncompressed, is not read; most apps also
    /// offer a PNG, which is far smaller. 4K screenshots are 33 MB.
    /// </summary>
    public const long MaxBitmapBytes = 128L * 1024 * 1024;

    private const int OpenAttempts = 10;
    private const int OpenRetryMilliseconds = 30;
    private const uint FirstRegisteredFormat = 0xC000;

    /// <summary>
    /// The clipboard's content, or <c>null</c> if it is concealed, empty,
    /// larger than <paramref name="maxItemBytes"/>, held by another app for
    /// too long, refused by <paramref name="mayRead"/>, or holds nothing Clipo keeps.
    /// </summary>
    /// <param name="maxItemBytes">The largest item the history keeps.</param>
    /// <param name="mayRead">Asked once the clipboard is open, before any data is read. No other app can change the clipboard then, so it can ask about the app whose data this is.</param>
    public static ClipboardContent? Read(long maxItemBytes, Func<bool> mayRead)
    {
        ArgumentNullException.ThrowIfNull(mayRead);
        if (!Open())
        {
            Trace.TraceWarning("the clipboard stayed busy; one copy was missed");
            return null;
        }
        try
        {
            if (!mayRead())
            {
                return null;
            }
            var formats = ListFormats();
            return IsConcealed(formats) ? null : ReadContent(formats, maxItemBytes);
        }
        catch (Exception e)
        {
            // Never log what was on the clipboard.
            Trace.TraceError($"reading the clipboard failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool Open()
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(0))
            {
                return true;
            }
            Thread.Sleep(OpenRetryMilliseconds);
        }
        return false;
    }

    private static HashSet<uint> ListFormats()
    {
        var formats = new HashSet<uint>();
        for (var format = NativeMethods.EnumClipboardFormats(0); format != 0; format = NativeMethods.EnumClipboardFormats(format))
        {
            formats.Add(format);
        }
        return formats;
    }

    private static bool IsConcealed(HashSet<uint> formats)
    {
        if (ConcealedFormats.IsConcealed(RegisteredNames(formats)))
        {
            return true;
        }
        return formats.Contains(ClipboardFormats.CanIncludeInHistory)
            && ReadBytes(ClipboardFormats.CanIncludeInHistory, 16) is { } value
            && ConcealedFormats.OptsOutOfHistory(value);
    }

    private static List<string> RegisteredNames(HashSet<uint> formats)
    {
        var names = new List<string>();
        var buffer = stackalloc char[256];
        foreach (var format in formats.Where(format => format >= FirstRegisteredFormat))
        {
            var length = NativeMethods.GetClipboardFormatName(format, buffer, 256);
            if (length > 0)
            {
                names.Add(new string(buffer, 0, length));
            }
        }
        return names;
    }

    private static ClipboardContent? ReadContent(HashSet<uint> formats, long maxItemBytes)
    {
        if (formats.Contains(NativeMethods.CfHdrop) && ReadFiles() is { } files)
        {
            return files;
        }
        if (formats.Contains(NativeMethods.CfUnicodeText) && ReadText(formats, maxItemBytes) is { } text)
        {
            return text;
        }
        return ReadImage(formats, maxItemBytes);
    }

    private static ClipboardContent.Files? ReadFiles()
    {
        var drop = NativeMethods.GetClipboardData(NativeMethods.CfHdrop);
        if (drop == 0)
        {
            return null;
        }
        var count = NativeMethods.DragQueryFile(drop, uint.MaxValue, null, 0);
        if (count == 0 || count > CaptureBuilder.MaxFiles)
        {
            return null;
        }
        var paths = new List<string>((int)count);
        for (uint index = 0; index < count; index++)
        {
            var length = NativeMethods.DragQueryFile(drop, index, null, 0);
            var path = new char[length + 1];
            fixed (char* buffer = path)
            {
                length = NativeMethods.DragQueryFile(drop, index, buffer, length + 1);
            }
            paths.Add(new string(path, 0, (int)length));
        }
        return new ClipboardContent.Files(paths);
    }

    private static ClipboardContent.Text? ReadText(HashSet<uint> formats, long maxItemBytes)
    {
        var plain = ReadBytes(NativeMethods.CfUnicodeText, maxItemBytes) is { } utf16 ? Terminated(Encoding.Unicode.GetString(utf16)) : null;
        if (string.IsNullOrEmpty(plain))
        {
            return null;
        }
        // Latin-1 accepts any bytes: RTF is ASCII with escapes.
        var rtf = formats.Contains(ClipboardFormats.Rtf) && ReadBytes(ClipboardFormats.Rtf, maxItemBytes) is { } rtfBytes
            ? Terminated(Encoding.Latin1.GetString(rtfBytes))
            : null;
        var html = formats.Contains(ClipboardFormats.Html) && ReadBytes(ClipboardFormats.Html, maxItemBytes) is { } htmlBytes
            ? CfHtml.ExtractFragment(htmlBytes)
            : null;
        return new ClipboardContent.Text(plain, rtf, html);
    }

    private static ClipboardContent.Image? ReadImage(HashSet<uint> formats, long maxItemBytes)
    {
        if (formats.Contains(ClipboardFormats.Png) && ReadBytes(ClipboardFormats.Png, maxItemBytes) is { } png)
        {
            return new ClipboardContent.Image(png, IsPng: true);
        }
        return formats.Contains(NativeMethods.CfDib) && ReadBitmap() is { } dib ? new ClipboardContent.Image(dib, IsPng: false) : null;
    }

    /// <summary>The bitmap, unless it is too big to decode; looks at its size before copying it.</summary>
    private static byte[]? ReadBitmap()
    {
        var handle = NativeMethods.GetClipboardData(NativeMethods.CfDib);
        if (handle == 0)
        {
            return null;
        }
        var size = (long)NativeMethods.GlobalSize(handle);
        var memory = NativeMethods.GlobalLock(handle);
        if (memory is null)
        {
            return null;
        }
        try
        {
            if (size == 0 || size > MaxBitmapBytes)
            {
                return null;
            }
            var data = new ReadOnlySpan<byte>(memory, (int)size);
            if (!Dib.TryReadSize(data, out var width, out var height) || (long)width * height > CaptureBuilder.MaxPixels)
            {
                return null;
            }
            return data.ToArray();
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>A copy of the global memory behind a clipboard format, or <c>null</c> if it is missing, empty or over <paramref name="maxBytes"/>.</summary>
    private static byte[]? ReadBytes(uint format, long maxBytes)
    {
        var handle = NativeMethods.GetClipboardData(format);
        if (handle == 0)
        {
            return null;
        }
        var size = (long)NativeMethods.GlobalSize(handle);
        if (size == 0 || size > maxBytes)
        {
            return null;
        }
        var memory = NativeMethods.GlobalLock(handle);
        if (memory is null)
        {
            return null;
        }
        try
        {
            return new ReadOnlySpan<byte>(memory, (int)size).ToArray();
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>The text up to the first NUL; clipboard blocks are often padded.</summary>
    private static string Terminated(string text)
    {
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }
}
