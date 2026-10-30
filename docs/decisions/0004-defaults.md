# 0004 — Defaults

Status: accepted

| Setting | Default |
|---|---|
| Global hotkey | Cmd+Shift+V (macOS), Ctrl+Shift+V (Windows) |
| History size | 200 items |
| Auto-paste | Off: selecting an item only restores it to the clipboard |

**Why.** Auto-paste needs the Accessibility permission on macOS and can paste into the wrong place, so it is opt-in. All three settings stay user-configurable.

**Consequences.** Cmd/Ctrl+Shift+V can collide with "paste without formatting" in some apps. The hotkey is configurable for that reason.
