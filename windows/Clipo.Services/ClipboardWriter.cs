using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Clipo.Core;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>Puts a stored item back on the Windows clipboard in the forms it was copied in.</summary>
[SupportedOSPlatform("windows")]
public static unsafe class ClipboardWriter
{
    private const uint Moveable = 0x2;
    private const uint DropEffectCopy = 1;
    private const int OpenAttempts = 10;
    private const int OpenRetryMilliseconds = 30;

    /// <summary>
    /// Replaces the clipboard's content with <paramref name="content"/>.
    /// Returns whether the clipboard took it. Rich text keeps its plain form
    /// beside it, so apps that cannot paste markup still get the text. An
    /// image goes on as PNG, plus <paramref name="bitmap"/> (a <c>CF_DIB</c>)
    /// for apps that only paste bitmaps.
    /// </summary>
    /// <param name="owner">A window of this app; the clipboard refuses data without an owner.</param>
    public static bool Write(nint owner, ItemContent content, byte[]? bitmap)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!Open(owner))
        {
            Trace.TraceWarning("the clipboard stayed busy; nothing was restored");
            return false;
        }
        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }
            return content switch
            {
                ItemContent.Text text => WriteText(text.TextValue),
                ItemContent.Image image => WriteImage(image.ImageValue, bitmap),
                ItemContent.Files files => WriteFiles(files.FilesValue),
                _ => false,
            };
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool Open(nint owner)
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                return true;
            }
            Thread.Sleep(OpenRetryMilliseconds);
        }
        return false;
    }

    private static bool WriteText(TextContent text)
    {
        var written = Set(NativeMethods.CfUnicodeText, Encoding.Unicode.GetBytes(text.Plain + '\0'));
        switch (text.Rich)
        {
            case { Format: RichTextFormat.Rtf } rtf:
                // Read as Latin-1, so written back the same way: RTF is ASCII with escapes.
                Set(ClipboardFormats.Rtf, Encoding.Latin1.GetBytes(rtf.Data + '\0'));
                break;
            case { Format: RichTextFormat.Html } html:
                Set(ClipboardFormats.Html, CfHtml.Build(html.Data));
                break;
        }
        return written;
    }

    private static bool WriteImage(ImageContent image, byte[]? bitmap)
    {
        var written = image.Format == ImageFormat.Png && Set(ClipboardFormats.Png, image.Data);
        if (bitmap is not null)
        {
            written = Set(NativeMethods.CfDib, bitmap) || written;
        }
        return written;
    }

    private static bool WriteFiles(FileRef[] files)
    {
        // Paths, not file contents: if a file moved since, the app being
        // pasted into says so, as it would for a copy made in Explorer.
        if (files.Length == 0 || !Set(NativeMethods.CfHdrop, DropFiles.Build([.. files.Select(file => file.Path)])))
        {
            return false;
        }
        Set(ClipboardFormats.PreferredDropEffect, BitConverter.GetBytes(DropEffectCopy));
        return true;
    }

    /// <summary>Hands a copy of <paramref name="data"/> to the clipboard, which then owns the memory.</summary>
    private static bool Set(uint format, byte[] data)
    {
        var memory = NativeMethods.GlobalAlloc(Moveable, (nuint)data.Length);
        if (memory == 0)
        {
            return false;
        }
        var target = NativeMethods.GlobalLock(memory);
        if (target is null)
        {
            NativeMethods.GlobalFree(memory);
            return false;
        }
        data.CopyTo(new Span<byte>(target, data.Length));
        NativeMethods.GlobalUnlock(memory);
        if (NativeMethods.SetClipboardData(format, memory) == 0)
        {
            Trace.TraceWarning($"the clipboard refused format {format} (Win32 error {Marshal.GetLastPInvokeError()})");
            NativeMethods.GlobalFree(memory);
            return false;
        }
        return true;
    }
}
