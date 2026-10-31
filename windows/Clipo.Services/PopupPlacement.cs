namespace Clipo.Services;

/// <summary>Where the popup opens.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// The popup's frame for a pointer at <paramref name="cursor"/> on a
    /// monitor whose usable area is <paramref name="workArea"/>. The popup
    /// hangs from the pointer, to its right in a left-to-right layout and to
    /// its left in a right-to-left one, and is moved as little as needed to fit
    /// on the monitor. If it is larger than the monitor, its top and leading
    /// edges stay visible.
    /// </summary>
    public static ScreenRect Frame(int width, int height, ScreenPoint cursor, ScreenRect workArea, bool rightToLeft)
    {
        var preferredLeft = rightToLeft ? cursor.X - width : cursor.X;
        // Clamped against the trailing edge first, so the leading edge wins.
        var left = rightToLeft
            ? Math.Min(Math.Max(preferredLeft, workArea.Left), workArea.Right - width)
            : Math.Max(Math.Min(preferredLeft, workArea.Right - width), workArea.Left);
        // Clamped against the bottom first, so the top wins.
        var top = Math.Max(Math.Min(cursor.Y, workArea.Bottom - height), workArea.Top);
        return new ScreenRect(left, top, left + width, top + height);
    }
}
