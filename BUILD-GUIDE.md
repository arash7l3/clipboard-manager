# Clipboard Manager — Build Guide

A menu-bar / system-tray clipboard history app for macOS and Windows with fully native UIs.

## 1. Goal

Never lose something you copied. The app lives only as a tray icon, remembers a configurable number of recent clipboard items, and shows them in a popup opened with a global hotkey. Picking an item puts it back on the clipboard (and optionally pastes it).

## 2. Requirements

### Core features

- Tray / menu-bar icon only: no main window, no Dock or taskbar entry.
- Clipboard watcher that captures every new copy.
- Configurable history size (for example 50 or 200). Oldest unpinned items are dropped first.
- Global hotkey opens a popup near the cursor; keyboard navigation (arrows, Enter, Esc) and mouse both work.
- Categories shown as tabs: All, Text, Images, Files, Pinned.
  - Text: plain and rich text, with links and code recognized.
  - Images: thumbnails.
  - Files: copied file paths with name, icon and size.
- Selecting an item restores it to the clipboard; optional auto-paste into the active app.

### Extra features

- Instant search in the popup.
- Pin / favorite items (never expire).
- Duplicate merge: copying the same content moves it to the top.
- Private mode toggle (capture paused).
- Ignore list of apps (for example password managers); skip items marked concealed by the source app.
- Auto-clear unpinned items after a chosen time.
- Paste as plain text.
- Quick-paste shortcuts for the first nine items.
- Snippets: saved text templates independent of history.
- Source app and time shown per item.
- Start at login.
- Export / import of history.

### Non-functional

- Native look and behavior on each OS. No web view, no web UI.
- English and Farsi UI with full RTL layout; Farsi text from other apps displays correctly.
- Light and dark mode follow the OS.
- Local-only storage, encrypted at rest. No cloud, no network calls.
- Low idle CPU and memory.
- Accessible: screen-reader labels and full keyboard use.

## 3. Architecture

```
core (Rust)                     shared engine, no UI
 ├─ history store (SQLite, encrypted)
 ├─ dedup, limits, pinning, categories, search
 ├─ ignore rules, auto-clear, private mode
 └─ exposed to the apps through UniFFI bindings

macos/ (Swift)                  windows/ (C#)
 ├─ SwiftUI + AppKit            ├─ WinUI 3 (WPF fallback)
 ├─ NSStatusItem menu bar       ├─ tray icon (H.NotifyIcon)
 ├─ NSPasteboard watcher        ├─ clipboard format listener
 ├─ global hotkey               ├─ RegisterHotKey
 └─ auto-paste via CGEvent      └─ auto-paste via SendInput
```

Languages: Rust, Swift, C#. No other languages.

Planned repo layout:

```
clipboard-manager/
  core/        Rust crate and UniFFI definitions
  macos/       Xcode project
  windows/     Visual Studio solution
  docs/        decision records
  BUILD-GUIDE.md
  CLAUDE.md
```

## 4. Constraints to respect

- Native controls only on both platforms.
- Clipboard contents are sensitive: never log them, never send them anywhere, encrypt them on disk, and keep the key in the OS keychain / credential store.
- Respect concealed-clipboard markers and the app ignore list.
- macOS: auto-paste needs the Accessibility permission; explain it clearly and degrade gracefully if denied. macOS has no clipboard change event, so poll the pasteboard change count (about 300 ms).
- Windows: use the event-driven clipboard listener; handle the tray overflow area.
- Large items (big images, huge files lists) must have size limits and must not freeze the UI.
- All user-facing text goes through each platform's localization system with English and Farsi entries.
- Business logic lives in the Rust core and is unit tested there; the UIs stay thin.
- Strict typing and linting: clippy and rustfmt for Rust, SwiftLint for Swift, analyzers for C#.

## 5. Phases

### Phase 0 — Setup and decisions

- Answer the open decisions below.
- Create the repo skeleton, CI skeleton and decision-record folder.
- Done when: structure exists, decisions are recorded in `docs/`.

### Phase 1 — Rust core

- Data model: items, kinds (text, image, file), pin flag, source app, timestamps.
- SQLite store with encryption, history limit, dedup, pinning, search, categories.
- Ignore rules, private mode, auto-clear.
- UniFFI bindings for Swift and C#.
- Done when: unit tests cover store, dedup, limits, search and auto-clear, and bindings generate for both targets.

### Phase 2 — macOS app

- Menu-bar app (no Dock icon) with watcher feeding the core.
- Global hotkey and floating popup panel with tabs, search, thumbnails.
- Select to restore, auto-paste with the Accessibility flow.
- Settings window, start at login, light/dark, English and Farsi with RTL.
- Done when: works on a clean Mac in LTR and RTL, light and dark, keyboard and mouse.

### Phase 3 — Windows app

- Tray app with event-driven clipboard listener feeding the core.
- Hotkey, popup, tabs, search, thumbnails, restore and auto-paste.
- Settings, start at login, light/dark, English and Farsi with RTL.
- Done when: works on Windows 10 and 11 in LTR and RTL, light and dark.

### Phase 4 — Extra features

- Snippets, quick-paste shortcuts, paste as plain text, export / import, source-app display.
- Done when: each feature works on both platforms and is covered by core tests where logic is shared.

### Phase 5 — Packaging, signing and CI

- macOS: signing, notarization, dmg.
- Windows: installer (MSIX or similar) and code signing.
- CI: Rust tests on every push; Mac and Windows builds.
- Done when: signed installers install and run on clean machines.

## 6. Open decisions

- Minimum OS versions (macOS, Windows 10 or only 11).
- WinUI 3 or WPF for Windows.
- Default hotkey and default history size.
- Auto-paste on by default, or restore to clipboard only.
- Whether to keep the Mac and Windows apps in one repo (current plan) or separate repos.
- App name and icon.
- Apple Developer account and Windows code-signing certificate availability.
