using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Clipo;

/// <summary>Sizes windows in effective pixels, the unit the layout uses.</summary>
internal static partial class WindowSize
{
    /// <summary>Sets the size of <paramref name="window"/>, scaled for the monitor's DPI.</summary>
    public static void Resize(AppWindow window, SizeInt32 size)
    {
        var dpi = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(window.Id));
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        window.Resize(new SizeInt32((int)(size.Width * scale), (int)(size.Height * scale)));
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
