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

    private const long ToolWindow = 0x80;

    /// <summary>Whether a window of class <paramref name="className"/> belongs to the shell rather than to an app.</summary>
    public static bool Contains(string? className) => className is not null && s_classNames.Contains(className);

    /// <summary>
    /// Whether a window counts as one the user works in: another process's,
    /// shown, not a tool window (the taskbar, its flyouts and Clipo's own
    /// helper windows are) and not one of the shell's surfaces.
    /// </summary>
    public static bool CountsAsApp(uint processId, uint ownProcessId, string? className, bool visible, long extendedStyle) =>
        processId != 0
        && processId != ownProcessId
        && visible
        && (extendedStyle & ToolWindow) == 0
        && !Contains(className);
}
