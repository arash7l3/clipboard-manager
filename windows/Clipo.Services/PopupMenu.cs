using System.Runtime.Versioning;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>
/// One line of a popup menu. A separator has no text and no id. An entry with
/// <paramref name="Submenu"/> opens it and has no id of its own.
/// </summary>
public sealed record MenuEntry(int Id, string Text, bool IsChecked = false, bool IsEnabled = true, IReadOnlyList<MenuEntry>? Submenu = null)
{
    public static MenuEntry Separator { get; } = new(0, "");

    public bool IsSeparator => Id == 0 && Submenu is null;
}

/// <summary>A native popup menu, as the tray icon shows.</summary>
[SupportedOSPlatform("windows")]
public static class PopupMenu
{
    private const uint Grayed = 0x1;
    private const uint Checked = 0x8;
    private const uint Popup = 0x10;
    private const uint SeparatorFlag = 0x800;
    private const uint ReturnCommand = 0x100;
    private const uint RightButton = 0x2;
    private const uint BottomAlign = 0x20;
    private const uint LayoutRtl = 0x8000;

    /// <summary>
    /// Shows <paramref name="entries"/> at the screen point (<paramref name="x"/>, <paramref name="y"/>)
    /// and returns the id of the one picked, or 0 if the menu was dismissed.
    /// Blocks until then, running its own message loop. Entry ids must be positive.
    /// </summary>
    public static int Show(MessageWindow owner, IReadOnlyList<MenuEntry> entries, int x, int y, bool rightToLeft)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entries);
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }
        try
        {
            Fill(menu, entries);
            // Without this the menu stays open when the user clicks elsewhere.
            NativeMethods.SetForegroundWindow(owner.Handle);
            var alignment = RightButton | BottomAlign | ReturnCommand | (rightToLeft ? LayoutRtl : 0);
            var picked = NativeMethods.TrackPopupMenu(menu, alignment, x, y, 0, owner.Handle, 0);
            NativeMethods.PostMessage(owner.Handle, NativeMethods.WmNull, 0, 0);
            return picked;
        }
        finally
        {
            // Destroys the submenus with it.
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void Fill(nint menu, IReadOnlyList<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                NativeMethods.AppendMenu(menu, SeparatorFlag, 0, null);
                continue;
            }
            var flags = (entry.IsChecked ? Checked : 0) | (entry.IsEnabled ? 0 : Grayed);
            if (entry.Submenu is { } children)
            {
                var submenu = NativeMethods.CreatePopupMenu();
                if (submenu != 0)
                {
                    Fill(submenu, children);
                    // For a popup entry the id is the submenu's handle.
                    NativeMethods.AppendMenu(menu, flags | Popup, (nuint)submenu, entry.Text);
                }
                continue;
            }
            NativeMethods.AppendMenu(menu, flags, (nuint)entry.Id, entry.Text);
        }
    }
}
