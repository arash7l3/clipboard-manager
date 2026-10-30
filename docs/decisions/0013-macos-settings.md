# 0013 — macOS settings: SwiftUI window over UserDefaults, login item, language by relaunch

Status: accepted

**Decision.**

- "Settings…" (Cmd+,) in the menu-bar menu opens one Settings window with three tabs: General, History and Privacy. The window is SwiftUI hosted in an `NSWindow`; the rest of the app stays AppKit (decision 0009). Every control reads and writes `UserDefaults` through `@AppStorage`; nothing else holds a copy of a setting.
- `SettingsCoordinator` watches `UserDefaults` and compares a snapshot of Clipo's settings with the one last applied, so unrelated writes do nothing. A change of appearance sets `NSApp.appearance`; a change of shortcut re-registers the hot key; a change of history size, auto-clear, private mode or ignored apps gives the core new limits and rules and deletes what has expired. Expired items are also deleted once an hour and at launch.
- Settings: start at login, appearance (match system, light, dark), language (match system, English, Farsi), the shortcut, auto-paste, items to keep (50 to 1000, default 200), auto-clear (never, 1, 7 or 30 days), private mode, and the user's list of ignored apps (typed bundle identifier or picked from an app bundle). The built-in password-manager list stays on and is not editable.
- Start at login uses `SMAppService.mainApp`. If macOS wants the user's approval, Settings says so and opens System Settings > Login Items.
- The language is stored as a Clipo setting and written to `AppleLanguages` in Clipo's own defaults domain; macOS reads that only at launch, so Settings offers "Restart" (a short `sh` helper reopens the app). With Farsi, AppKit and SwiftUI mirror the layout themselves; the popup already placed itself by `userInterfaceLayoutDirection` (decision 0011).
- The shortcut is one of five modifier sets on the V key (Cmd+Shift, Ctrl+Shift, Option+Shift, Cmd+Option, Ctrl+Option). If another app holds the chosen one, Clipo beeps, registers the old one again and puts the setting back.
- Turning auto-paste on without the Accessibility permission shows the explanation of decision 0012 right away, not at the first paste.

**Why.**

- `@AppStorage` keeps the controls and the stored values in step with no glue code, and the snapshot comparison keeps the core calls to real changes.
- A fixed list of shortcuts needs no key-recording control, which is easy to get wrong for non-Latin layouts and for keys the system already uses.
- Relaunching is the only way to change an app's language without private APIs, and it is what the system's own per-app language setting does.

**Consequences.**

- The shortcut cannot be set to another key than V. A free recorder can replace the picker later without changing how the choice is stored.
- Changing the language needs a restart; until then the window stays in the old one.
- Private mode is a Settings switch only. A menu-bar toggle belongs with the extras in phase 4.
- The core keeps rules and limits in memory only, so they are applied at every launch and are never stored in the history file.
