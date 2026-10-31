# 0015 — Windows capture, tray icon and shortcut: clipboard listener, native tray, RegisterHotKey

Status: accepted

**Decision.**

- **Change notification.** A message-only window (`MessageWindow`) is added with `AddClipboardFormatListener` and receives `WM_CLIPBOARDUPDATE`. Nothing is polled. Updates that share a clipboard sequence number count once, and what is on the clipboard when the listener starts is not captured.
- **Reading.** One change at a time on a background worker, in the order files (`CF_HDROP`), text (`CF_UNICODETEXT`, with `Rich Text Format` and the fragment of `HTML Format`), image (`PNG`, else `CF_DIB`). `OpenClipboard` is retried for about 300 ms because other apps hold it briefly.
- **Concealed data is never read.** A change is dropped before any content is read if the clipboard carries `ExcludeClipboardContentFromMonitorProcessing` or `Clipboard Viewer Ignore`, or a `CanIncludeInClipboardHistory` DWORD of 0. Windows' own clipboard history honors the same markers.
- **Source app.** The clipboard's owner window (`GetClipboardOwner`), found on the UI thread as soon as the change is noticed; the foreground window if the owner is already gone. The app id is the executable's file name (`KeePassXC.exe`), the display name its file description.
- **Ignore list.** Built-in password managers by executable name (`CaptureSettings.DefaultIgnoredApps`), plus private mode. The app gives the rules to the core with `SetRules` each time the history opens; if that fails, nothing is captured. The watcher asks the rules before reading, and the core applies them again in `Record`. With the clipboard open, the watcher also asks about the app that owns it then, because a newer copy may have replaced the one it was told about (decision 0026). Persisting and editing them is step 3.4.
- **Limits** as on macOS (decision 0010): at most 1000 files, 100 million pixels, nothing over the core's `MaxItemBytes`. A bitmap over 128 MiB uncompressed is not read. Images are stored as PNG: a PNG on the clipboard as it is, a DIB re-encoded through the Windows Imaging Component. Thumbnails are PNGs up to 256 px.
- **Tray icon.** `Shell_NotifyIcon` with notify-icon version 4, on the same message window, from `Clipo.ico` (made from the SVG by `assets/make-windows-icon.py`). Click or keyboard select opens Clipo; the context menu is a native popup menu with Open (showing the shortcut) and Quit, laid out right to left when the UI language is. The icon is added again when Explorer restarts (`TaskbarCreated`).
- **Shortcut.** Ctrl+Shift+V (decision 0004) through `RegisterHotKey` with `MOD_NOREPEAT`, delivered as `WM_HOTKEY` to the message window. If another app holds it, the status window says so and the tray icon is the way in.
- The tray icon, its Show Clipboard History item and the shortcut open the history popup (decision 0016). Closing the status window hides it; Quit in the tray menu ends the app.
- **Where code lives.** Everything that talks to Win32 is in `Clipo.Services` (plain `net10.0`, so it compiles and its logic is tested on any OS). `Clipo` holds only what needs WinUI: `ImageProcessor` (WinRT imaging), `Shell` (wiring) and the windows.

**Why.**

- Windows reports every clipboard change and who owns the clipboard, so polling and guessing the frontmost app (needed on macOS) would be worse here.
- Not reading concealed data keeps secrets out of Clipo's memory, not just out of the database.
- The native tray API needs no extra package, and the single message window serves the clipboard, the shortcut and the tray alike.
- Keeping the Win32 code out of the WinUI project means the parsing, limits and recording logic have unit tests that run on Linux, where only CI compiles WinUI.

**Consequences.**

- Ignore-list entries are executable names; two apps with the same file name are not told apart, and a packaged (Store) app may show up as its host process.
- The tray menu is a classic Win32 menu: it does not follow dark mode until step 3.4 looks at it.
- Windows 11 may put a new tray icon in the overflow ("hidden icons") until the user drags it out.
- Ctrl+Shift+V is "paste as plain text" in several apps. While Clipo owns the shortcut those apps no longer get it; the shortcut becomes configurable in step 3.4.
- A clipboard that another app keeps open for more than about 300 ms loses that copy.
- Like the rest of the Windows app, this has been compiled and unit-tested in CI but not run on a Windows PC yet.
