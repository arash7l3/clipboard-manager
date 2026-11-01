# 0019 — macOS extras: Option for plain text, Cmd+digit quick paste, snippets in the menu

Status: accepted

**Decision.**
- **Paste as plain text:** hold Option when choosing an item (Option+Return, or Option+click). The app writes `ClipStore.plainText` to the clipboard instead of the stored formats. An image has no plain text and beeps.
- **Quick paste:** Cmd+1 to Cmd+9 in the open popup choose the first nine rows of the current tab; each row shows its shortcut. No global shortcuts: they would take nine more key combinations from other apps.
- **Snippets:** managed in a Settings tab (add, edit, delete) and pasted from a "Snippets" submenu of the menu-bar icon. A snippet is written to the clipboard as plain text and pasted like an item when auto-paste is on, but it is not history and does not move anything to the top.
- **Export / import:** a "Data" Settings tab. Export asks for confirmation first, saying the file is not encrypted (ADR 0018), and writes it readable by the user only. Import reports what was added, or that the file is invalid or from a newer Clipo.
- **Source app** was already on each row's second line (step 2.3); nothing changed.

**Why.** Option and Cmd+digit keep the popup's one-keyboard-focus design (ADR 0011). A menu is the lightest place for snippets that does not mix them into history ranking or tabs.

**Consequences.** Snippets cannot be searched from the popup yet; they are a short list in a menu.

**Update (0.1.4).** A snippet picked from the menu is now recorded in the history like any copy (decision 0029).
