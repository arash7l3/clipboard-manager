# 0034 — Pinned items stay on top; the export carries item names and settings

Status: accepted

**Decision.**
- **Pinned first.** `Store::search` and `Store::previews` order by `pinned DESC, copy_order DESC`, so pinned items stay above every unpinned one in every tab and search, and a new copy goes below them. Unpinning puts an item back in its place by copy time. `Store::list`, which the export reads, keeps plain copy order.
- **Export version 2.** Each item carries `option<str title>` after its source app, and a list of settings (`str key`, `str value`) follows the snippets (format in `core/src/store/transfer.rs`). Version 1 files still import, without names or settings. `Store::export_state(settings)` writes it and `Store::import_state` returns the settings with the summary. `export` and `import` stay as they were for callers that need no settings; `export` now writes version 2 with no settings.
- **Names on import.** A new item gets its exported name. An item already stored keeps its own name and takes the exported one only if it has none.
- **Settings are the apps'.** The core only carries the pairs. Each app maps its settings to text (`SettingsTransfer` on macOS and Windows). Keys without a platform prefix mean the same on both, so a file moves between them: `history.maxItems`, `history.autoClearDays`, `capture.privateMode`, `paste.autoPaste`, `interface.appearance` (`system`, `light`, `dark`), `interface.language` (`system`, `en`, `fa`) and `popup.startTab` (`all`, `text`, `images`, `files`, `pinned`, `lastUsed`). The shortcut and the user's ignored apps differ per platform and are `macos.hotkey` / `macos.ignoredApps` and `windows.hotkey` / `windows.ignoredApps` (apps one per line). On import, unknown keys and values a setting cannot take are skipped and that setting stays as it was. Start at login is the system's, not Clipo's, and is not exported.
- **Applying.** On macOS the values go into `UserDefaults`, where `SettingsCoordinator` picks them up as if changed in Settings; a new language also sets `AppleLanguages`. On Windows the imported settings go through the same path as a change in the Settings window (`Changed`), which saves and applies them and undoes a shortcut another app holds.

**Why.** The user asked that pinned items never sink below newer copies, and that an export restore the whole state, not only the content: decision 0033 had left names out of the export. A key and value list keeps the core free of platform settings, lets a newer app add settings without a new format version, and lets an older app skip what it does not know.

**Consequences.** An export made by this version cannot be read by an older Clipo (it refuses version 2 as too new). Settings carry no secrets, so the export's warning is unchanged.
