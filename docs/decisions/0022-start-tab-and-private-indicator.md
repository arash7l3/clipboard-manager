# 0022 — Start tab setting and a visible private mode

Status: accepted

**Decision.**
- **Start tab:** Settings > General has "Open history on": All, Text, Images, Files, Pinned or Last used tab (default All, as before). Last used tab is the tab the popup was on when it was last open, kept while Clipo runs; after a restart it starts on All. Windows keeps it in the model, which already holds the category between openings. macOS does the same (`PopupModel.category` before `reset(to:)`).
- **Private mode is quick to reach:** a checkable "Private Mode" item in the menu-bar menu (macOS) and the tray menu (Windows). Both write the same setting as the check box in Settings, so the three always agree.
- **Private mode is visible:**
  - the menu-bar icon becomes an orange crossed-out eye (macOS); the tray icon becomes a grey tile with an orange badge (`ClipoPrivate.ico`, made by `assets/make-windows-icon.py ... private`), and its tooltip says private mode is on;
  - the popup shows a one-line note at the top while it is on.

**Why.** Private mode lived only in a Settings check box, and nothing showed that it was on, so it was easy to forget it and wonder why nothing was saved. The icon changes colour and shape, not only a badge, so it still reads in grey tones.

**Consequences.** "Last used tab" is not saved to disk; saving it would need a settings key written on every tab change, and it is easy to add if wanted. The macOS icon colour uses `NSStatusBarButton.contentTintColor`; if a macOS version ignores it, the changed symbol still shows (see `macos-verification.md`).

**Update.** The macOS icon is now drawn in its final colour (a non-template orange symbol) instead of a template image with `contentTintColor`, and it is never `nil`, after a report that the icon disappeared from the menu bar in private mode. Private mode only changes the icon; the hotkey and the menu keep working.
