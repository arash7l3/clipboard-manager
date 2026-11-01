namespace Clipo.Services;

/// <summary>The parts of the Windows shell that come to the front on the way to Clipo's tray menu.</summary>
public static class ShellSurfaces
{
    private static readonly HashSet<string> s_classNames = new(StringComparer.Ordinal)
    {
        // The taskbar, on the main screen and on the others.
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        // The hidden-icons flyout: Windows 10, then Windows 11.
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        // Start, search and the other flyouts of Windows 11.
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
    };

    /// <summary>Whether a window of class <paramref name="className"/> belongs to the shell rather than to an app.</summary>
    public static bool Contains(string? className) => className is not null && s_classNames.Contains(className);
}
