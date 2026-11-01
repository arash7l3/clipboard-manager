# 0028 — macOS icon as an .icns file, a badge for private mode, and the tray menu's focus on Windows

Status: accepted

**Decision.**
- **macOS app icon:** the icon is `macos/Clipo/AppIcon.icns`, named by `CFBundleIconFile` in `macos/Clipo-Info.plist` (merged with the generated Info.plist). The asset catalog is gone. `assets/make-windows-icon.py --icns` writes the file with every size from 16 to 1024 pixels, from the same shapes as before. `make-dmg.sh` fails if the built app has no icon file.
- **macOS menu-bar icon in private mode:** the usual clipboard symbol with a crossed-out eye cut into its bottom-right corner, one template image, instead of an orange eye on its own. This replaces the icon part of decision 0022.
- **Windows tray menu:** the menu is owned by a hidden top-level window (`MessageWindow(topLevel: true)`), not the message-only one, because Windows wants a tray menu's owner in front and a message-only window cannot be. `ForegroundTracker` remembers the last app window in front, skipping Clipo and the taskbar, and picking a snippet gives that window its focus back before the snippet is written and Ctrl+V is sent.

**Why.**
- The 0.1.2 app carried the icon in its asset catalog and Info.plist named it, yet Spotlight, the Accessibility list in Privacy & Security and the About panel all showed the blank generic icon. A plain .icns file is the form every macOS version reads.
- An eye on its own did not say which app it belonged to.
- On Windows, picking a snippet in the tray menu put nothing on the clipboard and pasted nothing. Right-clicking the tray icon puts the taskbar in front, so Ctrl+V went to the taskbar; and the message-only owner could not come to the front as a tray menu's owner must.

**Consequences.**
- The private-mode icon is no longer orange; the badge, the tooltip and the checked menu item show it. Template images follow the menu bar's tint in light and dark mode.
- macOS may keep showing a cached blank icon for an app it saw before. Replacing the app in Applications usually refreshes it; if not, `killall Dock` or a restart does. The Accessibility list may need Clipo removed and added again.
- The foreground watcher skips the Windows shell by window class (`ShellSurfaces`); a shell surface missing from that list would receive the paste, as before.
