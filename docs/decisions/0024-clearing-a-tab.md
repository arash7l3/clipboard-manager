# 0024 — Clearing a tab: a Clear button with inline confirmation, and Clear History in the menus

Status: accepted

**Decision.**
- **Core:** `ClipStore.clear(category)` deletes the unpinned items listed under a tab and returns how many. All clears every unpinned item, Text, Images and Files clear that kind, and Pinned clears nothing. Pinned items and snippets are never deleted. Cleared content is overwritten in the file, as with `delete`. Search text plays no part: Clear empties the whole tab, not only the rows a search shows.
- **Popup:** a trash button at the trailing end of the tab row clears the current tab. The first click turns it into a red "Clear?"; a second click within 3 seconds clears. After that, or after switching tabs or closing the popup, it goes back to the trash icon. The tooltip and screen-reader name say what will go ("Clear unpinned images"). The button is hidden on the Pinned tab. The popup stays open and lists the rows again.
- **Menus:** "Clear History…" in the menu-bar menu (macOS) and the tray menu (Windows) clears every unpinned item after a confirmation dialog (an `NSAlert`, or a `ContentDialog` in the status window). Cancel is the default button. It is disabled (macOS) or opens the status window (Windows) while the history is not open.

**Why.** The only ways to remove history were one row at a time (not offered in the UI) or auto-clear. The confirmation in the popup is inline because an alert or dialog would take the focus and close the popup, which closes when it loses focus (ADRs 0011, 0016). The menu path has no popup to keep open, so it uses a normal dialog. No keyboard shortcut: clearing is rare and cannot be undone.

**Consequences.** The search field keeps the keyboard, so the button is reached with the mouse or a screen reader, like the tabs. Clearing cannot be undone; pinning is the way to keep something.
