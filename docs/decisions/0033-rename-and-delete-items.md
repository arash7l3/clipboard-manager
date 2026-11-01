# 0033 — Renaming and deleting single items from the popup

Status: accepted

**Decision.**
- **Core:** schema version 4 adds a nullable `title` column to `items`. `ClipStore.set_title(id, title)` trims the name and stores it; a blank name stores `NULL`, which brings back the content's own first line. `ItemPreview.title` carries it to the popup. Search also matches the name. The content is never changed, so pasting a renamed item pastes what was copied. A repeated copy of the same content keeps the name. The name lives in the encrypted database with the item and goes when the item goes. Export does not carry names yet.
- **Popup rows:** two buttons at the trailing end of every row, a pencil (rename) and a trash can (delete), on macOS and Windows. Delete removes the item at once with the existing `ClipStore.delete`; it does not ask, because it touches one row the user is looking at. Pinned items can be deleted this way too.
- **Renaming in place:** the pencil swaps the row's title for a field holding the current name, selected. Typing replaces it, Return or Enter saves, Esc keeps the old name. Saving an unchanged name does nothing. On macOS the field is a real `NSTextField` that takes the keyboard from the search field until the rename ends. On Windows the popup never takes the focus (ADR 0032), so the name is typed through the popup's keys like the search (`PopupRename`), and End keeps the old name to type after it. Listing the rows again (a new copy, a tab switch, closing the popup) ends a rename without saving.

**Why.** Clear only empties a whole tab, and the user asked for a way to remove one item and give items names. An inline field, like the Clear button's inline confirmation (ADR 0024), avoids an alert or dialog that would take the focus and close the popup.

**Consequences.** The buttons are reached with the mouse or a screen reader; there are no new keyboard shortcuts. Deleting cannot be undone.
