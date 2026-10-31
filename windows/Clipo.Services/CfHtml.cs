using System.Globalization;
using System.Text;

namespace Clipo.Services;

/// <summary>The "HTML Format" clipboard format: a header of byte offsets, then UTF-8 HTML.</summary>
public static class CfHtml
{
    /// <summary>
    /// The HTML fragment the source marked as the copied part, or
    /// <c>null</c> if the header does not describe a valid one.
    /// </summary>
    public static string? ExtractFragment(ReadOnlySpan<byte> data)
    {
        // The offsets are plain ASCII digits in the header lines.
        if (!TryReadOffset(data, "StartFragment:", out var start) || !TryReadOffset(data, "EndFragment:", out var end))
        {
            return null;
        }
        if (start < 0 || end < start || end > data.Length)
        {
            return null;
        }
        var fragment = Encoding.UTF8.GetString(data[start..end]);
        return string.IsNullOrWhiteSpace(fragment) ? null : fragment;
    }

    private static bool TryReadOffset(ReadOnlySpan<byte> data, string name, out int offset)
    {
        offset = 0;
        // The header is short; its fields are all before the HTML starts.
        var header = data[..Math.Min(data.Length, 512)];
        var index = header.IndexOf(Encoding.ASCII.GetBytes(name));
        if (index < 0)
        {
            return false;
        }
        var digits = header[(index + name.Length)..];
        var length = 0;
        while (length < digits.Length && digits[length] is >= (byte)'0' and <= (byte)'9')
        {
            length++;
        }
        return length > 0 && int.TryParse(Encoding.ASCII.GetString(digits[..length]), NumberStyles.None, CultureInfo.InvariantCulture, out offset);
    }
}
