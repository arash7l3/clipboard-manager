# 0020 — Windows extras: Shift for plain text, Ctrl+digit quick paste, snippets in the tray menu

Status: accepted

**Decision.**
- **Paste as plain text:** hold Shift when choosing an item (Shift+Enter, Shift+click, or Ctrl+Shift+digit). The app writes `ClipStore.PlainText` to the clipboard instead of the stored formats. An image has no plain text and beeps. Shift is the Windows convention (Ctrl+Shift+V); Option, which ADR 0019 uses on the Mac, does not exist here and Alt opens menus.
- **Quick paste:** Ctrl+1 to Ctrl+9 in the open popup choose the first nine rows of the current tab; each row shows its key. No global shortcuts, as on the Mac.
- **Snippets:** managed in a Settings tab (add, edit, delete) and pasted from a "Snippets" submenu of the tray menu. A snippet is written as plain text and pasted like an item when auto-paste is on, but it is not history and moves nothing to the top. Names in the menu are cut to 40 characters and `&` is doubled so Windows does not read it as a mnemonic.
- **Export / import:** a "Data" Settings tab. Export asks for confirmation first, saying the file is not encrypted (ADR 0018), then writes through the system save dialog. Import reads through the open dialog, refuses files over 512 MiB without reading them into the core, and reports what was added, or that the file is invalid or from a newer Clipo.
- **Source app** was already on each row's second line (step 3.3); nothing changed.

**Why.** Shift and Ctrl+digit keep the popup's one-keyboard-focus design (ADR 0016). The tray menu is the lightest place for snippets that does not mix them into history ranking or tabs.

**Consequences.**
- Snippets cannot be searched from the popup yet; they are a short list in a menu.
- The export file is written wherever the user chose; Windows gives it that folder's access rules, and Clipo cannot make it user-only the way the Mac app does. The warning dialog and the note in Settings say it holds everything in the clear.
- Compiled only by CI (no Windows machine here); the checklist for a first run on a PC is in the PR.

**Update (0.1.4).** A snippet picked from the tray menu is now recorded in the history like any copy (decision 0029).
