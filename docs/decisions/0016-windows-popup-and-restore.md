# 0016 — Windows popup, restore and auto-paste: a WinUI window that takes the keyboard, SendInput for Ctrl+V

Status: accepted

**Decision.**

- **Popup.** A borderless WinUI 3 window (`PopupWindow`): no title bar, always on top, not in Alt+Tab or the taskbar, acrylic backdrop. It opens at the mouse pointer, to its right in left-to-right languages and to its left in right-to-left ones, moved just enough to fit the monitor's work area (`PopupPlacement`). Its size is 420 × 520 effective pixels at the monitor's scale.
- **Opening and closing.** Ctrl+Shift+V opens and closes it; a click on the tray icon or Show Clipboard History in the tray menu opens it. It closes on Esc, Alt+F4, the shortcut again, a pick, and when it loses the front (a click elsewhere). While the history is not open, these show the status window instead, which says why.
- **Focus.** Unlike the macOS panel (decision 0011), the popup is activated: Windows has no non-activating window that still takes typing. Before it opens, Clipo remembers the window in front. Esc and a pick hand the front back to that window before the popup hides, while Windows still allows Clipo to do so; a click elsewhere does not, because the user already chose a window.
- **Controls and keys** as on macOS: a search field that keeps the keyboard, tabs (All, Text, Images, Files, Pinned) in a `SelectorBar`, and a `ListView` of rows. Up, Down, Page Up, Page Down, Home and End move the selection; Tab and Shift+Tab switch tabs; Enter picks; a click on a row picks it. Screen readers are told about the newly selected row with a UI Automation notification, because the focus stays in the search field.
- **Rows** come from the core's `Previews`, as on macOS: an icon (Segoe Fluent Icons) or the stored thumbnail, the first line of a text or an image's size or the first file's name, and the source app and copy time. The model (`PopupModel`), row text (`PopupText`) and placement are in `Clipo.Services` and unit tested; the popup opens on All with an empty search, searches after an 80 ms pause, keeps only the latest answer, and refreshes on a new copy, keeping the selection.
- **Restore.** A pick loads the full item off the UI thread and writes it to the clipboard in the forms it was copied in: `CF_UNICODETEXT` with `Rich Text Format` or `HTML Format` beside it; `PNG` plus a `CF_DIB` made with the Windows Imaging Component, for apps that do not paste PNG; `CF_HDROP` with `Preferred DropEffect` set to copy. The watcher skips Clipo's own write (by clipboard sequence number), and the item moves to the top with the core's `Touch`. If the item is gone or the clipboard refuses it, Clipo plays the default beep.
- **Auto-paste** is off by default (decision 0004) and turned on with Paste After Choosing in the tray menu. It is saved in `%LOCALAPPDATA%\Clipo\settings.json` (`AppSettings`), which step 3.4 extends. When on, Clipo waits 120 ms after handing the front back, then sends Ctrl+V with `SendInput`.

**Why.**

- An activated window is the only way to type into the search field. Handing the front back explicitly, while Clipo still holds it, is what makes Esc and auto-paste land in the right app; Windows refuses a background app that tries later.
- Windows needs no permission for `SendInput`, so there is no Accessibility flow as on macOS. `VK_V` follows the keyboard layout, so the paste is Ctrl+V on Dvorak too.
- Writing PNG and a bitmap covers both modern apps (which keep transparency from the PNG) and older ones that only read `CF_DIB`.
- A JSON file next to the history needs no package identity, which the unpackaged app (decision 0014) does not have.

**Consequences.**

- Windows does not let a program send input to a window running as administrator unless it runs as administrator too. Auto-paste into such a window does nothing; the item is still on the clipboard.
- If Windows refuses to bring the popup to the front (its focus-stealing rules), the popup shows without the keyboard. The shortcut closes it.
- Opening from the tray icon remembers the taskbar as the window in front, so Esc and auto-paste go there; the shortcut is the way to paste into an app.
- Between the pick and the end of the 120 ms pause the user could switch windows and get the paste there: the reason auto-paste is off by default.
- The bitmap copy of an image is 32-bit without a meaningful alpha channel for most readers; apps that read PNG keep transparency.
- Like the rest of the Windows app, compiled and unit-tested in CI, not yet run on a Windows PC.
