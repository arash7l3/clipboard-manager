# 0021 — Pinning from the popup: Cmd/Ctrl+P and a row context menu

Status: accepted

**Decision.**
- **How:** select a row and press Cmd+P (macOS) or Ctrl+P (Windows), or right-click a row and choose Pin / Unpin. The right-click also selects the row. The menu item is labelled by the row's current state.
- **After pinning** the popup stays open and lists the rows again with the same item selected. In the Pinned tab, unpinning removes the row from the list. A failed change beeps.
- **Core:** nothing new. `ClipStore.setPinned` has existed since step 1.3; pinned items are exempt from the history limit and auto-clear. Unpinning can evict items past the limit, so the Windows app also recounts the history.

**Why.** Steps 2.3 and 3.3 added the Pinned tab, the pin mark and the pin-aware limits, but no way to pin: the tab could never hold anything. The keyboard shortcut keeps the popup's one-focus design (ADRs 0011, 0016); the context menu is the discoverable path.

**Consequences.** On the Mac, a context menu opened from the non-activating panel may take key status away and close the popup; Cmd+P is the reliable path until that is tried on a Mac (see `macos-verification.md`). On Windows the menu is a XAML flyout in the same window, and the search field gets the focus back when it closes.
