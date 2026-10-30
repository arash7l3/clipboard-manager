# 0012 — macOS restore and auto-paste: write back what was copied, paste with Cmd+V only if allowed

Status: accepted

**Decision.**

- Picking an item (Enter or a click) loads the full item by id off the main thread and writes it back to the general pasteboard in the forms it was copied in: plain text with its RTF or HTML beside it, the image in its stored encoding, or the files as file URLs in clipboard order.
- Clipo's own write is not captured again: the watcher is told to treat the pasteboard as seen right after the write. The picked item is moved to the top of the history with the core's `touch` instead, so it keeps its original source app and pin.
- Auto-paste is the `autoPaste` setting in `UserDefaults`, off unless turned on (decision 0004). The Settings window (step 2.5) will edit it. When it is on and the Accessibility permission is granted, Clipo waits 120 ms for the app underneath to become the key window again and posts Cmd+V (key down and up, with only the Command flag) at the HID event tap.
- When auto-paste is on and the permission is missing, the item is still restored. Once per launch an alert explains why the permission is needed, that Clipo uses it for nothing else and that Command-V pastes by hand. Its button asks macOS for the permission, which lists Clipo under Privacy & Security > Accessibility and shows the system's own request. Without the setting on, Clipo never asks for the permission.
- If the item is gone or the pasteboard refuses it, Clipo beeps and leaves the clipboard as it was.

**Why.**

- The popup never activates Clipo (decision 0011), so the target app is still frontmost and a plain Cmd+V reaches it.
- Re-capturing the write would record it with the frontmost app as the source, and would drop it altogether if that app is on the ignore list. `touch` keeps the history right without either problem.
- macOS drops key events from apps without the permission and tells nobody, so Clipo checks the permission first instead of pretending to paste.

**Consequences.**

- The paste key is the one at the physical "V" position. On layouts that move V (Dvorak, for one) auto-paste presses the wrong shortcut; restore to the clipboard still works.
- Files are restored as paths. A file that moved or was deleted since it was copied fails in the app being pasted into.
- A rich text kept as RTF is written back from its UTF-8 form; RTF is ASCII with escapes, so nothing is lost for text copied on a Mac.
- Between a pick and the end of the 120 ms pause the user could switch apps and receive the paste there. This is the cost of pasting for them, and the reason auto-paste is off by default.
