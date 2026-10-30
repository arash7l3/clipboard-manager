# 0011 — macOS popup: Carbon hot key, non-activating panel, previews from the core

Status: accepted

**Decision.**

- The global shortcut (Cmd+Shift+V, decision 0004) is registered with Carbon's `RegisterEventHotKey`. If another app holds it, the shortcut is missing from the menu and "Show Clipboard History" in the menu still opens the popup.
- The popup is a borderless `NSPanel` with `.nonactivatingPanel`, at the pop-up menu level, on every Space and over full-screen apps. It becomes key without activating Clipo, so the app the user was in stays frontmost.
- It opens at the mouse pointer, to its right in left-to-right languages and to its left in right-to-left ones, moved just enough to fit on the screen. It closes on Esc, on a click outside it, on the shortcut again, and after an item is picked.
- AppKit throughout: an `NSSearchField`, an `NSSegmentedControl` for the tabs (All, Text, Images, Files, Pinned) and a view-based `NSTableView`. The search field keeps the keyboard: Up, Down, Page Up, Page Down, Home and End move the selection, Tab and Shift+Tab switch tabs, Enter picks, Esc closes. A click on a row picks it.
- Rows come from a new core call, `ClipStore.previews(query)`: the item's id, pin, source app and copy time, the first 1000 characters of a text, an image's size and thumbnail, or the first 10 files and their count. The full item is loaded by id only when it is picked. The core also guesses whether a text is a link or code, for the row's icon and screen-reader label.
- The popup opens on the All tab with an empty search every time. Typing searches after an 80 ms pause; only the answer to the latest question is shown. A new copy refreshes the list while the popup is open and keeps the selected item selected.

**Why.**

- `RegisterEventHotKey` needs no Accessibility permission and swallows the key press, so the frontmost app never sees Cmd+Shift+V. A global event monitor needs Accessibility and cannot swallow it.
- Auto-paste (step 2.4) sends Cmd+V to the frontmost app. If the popup activated Clipo, it would first have to hand activation back and wait for it.
- `search` returns whole items: up to 32 MB per image and the full text. Loading that for every keystroke would make typing lag. A preview holds at most 1000 characters of text, a thumbnail of up to 256 pixels or 10 file paths.
- The search field never loses focus, so typing always searches, whatever the user clicked last.

**Safeguards.**

- Opening the popup clears the old rows at once, so Enter cannot pick an item left from last time before the list loads.
- Previews implement `Debug` without their text, paths or bytes, like the other content types.
- Because the selection moves while focus stays in the search field, the popup asks VoiceOver to read the newly selected row.

**Consequences.**

- Picking an item closes the popup and restores it to the clipboard (decision 0012).
- The link and code hint is a guess from the first 1000 characters. It changes only the icon and the spoken kind, never what is stored or restored.
- Tab switches tabs inside the popup, so moving keyboard focus between its controls is not possible; the tabs remain reachable with the mouse and VoiceOver.
- The shortcut is fixed until the Settings window (step 2.5) makes it configurable.
