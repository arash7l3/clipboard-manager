# Clipo — Work Plan

Step-by-step plan derived from [BUILD-GUIDE.md](../BUILD-GUIDE.md). Refer to a step by its ID, for example "do 1.3".

## Decisions

| Decision | Choice |
|---|---|
| App name | Clipo |
| Minimum OS | macOS 14, Windows 11 |
| Windows UI | WinUI 3 (WPF as fallback) |
| Default hotkey | Cmd/Ctrl+Shift+V |
| Default history size | 200 items |
| Auto-paste | Off by default (restore to clipboard only) |
| Repo layout | One monorepo |
| Signing | No Apple Developer account or Windows certificate yet; unsigned builds until available |

## Steps

Models: Opus 5.5 for design, security and tricky integration; Sonnet 5.5 for regular implementation; Haiku 5.5 for trivial scaffolding.

| ID | Phase | Step | Model | Effort | Session | Status |
|---|---|---|---|---|---|---|
| 0.1 | 0 Setup | Decision records in `docs/` | Sonnet 5.5 | low | S1 (with 0.2) | Done |
| 0.2 | 0 Setup | Repo skeleton, `.gitignore`, README, Clipo icon | Haiku 5.5 | low | S1 | Done |
| 1.1 | 1 Rust core | Crate setup, data model | Opus 5.5 | high | S2 (with 1.2) | Done |
| 1.2 | 1 Rust core | Encrypted SQLite store, keychain key interface | Opus 5.5 | xhigh | S2 | Done |
| 1.3 | 1 Rust core | Dedup, limits, pinning, search, categories, tests | Sonnet 5.5 | high | S3 (with 1.4) | Done |
| 1.4 | 1 Rust core | Ignore rules, private mode, auto-clear, tests | Sonnet 5.5 | medium | S3 | Done |
| 1.5 | 1 Rust core | UniFFI bindings for Swift and C#, packaging script | Opus 5.5 | high | S4 | Done |
| 1.6 | 1 Rust core | Core review (correctness, no clipboard content in logs) | Opus 5.5 | high | S5 (fresh) | Done |
| 2.1 | 2 macOS | Xcode project, menu-bar shell, link core | Opus 5.5 | high | S6 | Done |
| 2.2 | 2 macOS | Pasteboard watcher, concealed markers, ignore list | Sonnet 5.5 | high | S7 | Done |
| 2.3 | 2 macOS | Hotkey, popup, tabs, search, thumbnails, keyboard nav | Opus 5.5 | high | S8 (with 2.4) | Done |
| 2.4 | 2 macOS | Restore, optional auto-paste, Accessibility flow | Sonnet 5.5 | high | S8 | Done |
| 2.5 | 2 macOS | Settings, login item, light/dark, English and Farsi RTL | Sonnet 5.5 | high | S9 | Done |
| 2.6 | 2 macOS | Verification: run app, RTL, dark mode, accessibility | Sonnet 5.5 | medium | S10 (fresh) | Audited; manual run on a Mac pending ([checklist](macos-verification.md)) |
| 3.1 | 3 Windows | WinUI 3 solution, link core via C# bindings | Opus 5.5 | high | S11 |  |
| 3.2 | 3 Windows | Clipboard listener, tray icon, RegisterHotKey | Sonnet 5.5 | high | S12 (with 3.3) |  |
| 3.3 | 3 Windows | Popup, tabs, search, thumbnails, restore, auto-paste | Opus 5.5 | high | S12 |  |
| 3.4 | 3 Windows | Settings, login item, light/dark, English and Farsi RTL | Sonnet 5.5 | high | S13 |  |
| 4.1 | 4 Extras | Core: snippets, export/import, plain-text, source app | Sonnet 5.5 | high | S14 |  |
| 4.2 | 4 Extras | macOS UI for extras, quick-paste shortcuts | Sonnet 5.5 | medium | S15 |  |
| 4.3 | 4 Extras | Windows UI for extras | Sonnet 5.5 | medium | S16 |  |
| 5.1 | 5 Packaging | CI: Mac and Windows builds, caching, lint gates | Sonnet 5.5 | medium | S17 |  |
| 5.2 | 5 Packaging | macOS signing, notarization, dmg | Opus 5.5 | high | S18 |  |
| 5.3 | 5 Packaging | Windows MSIX installer and signing | Sonnet 5.5 | high | S19 |  |
| 5.4 | 5 Packaging | Final security and privacy review | Opus 5.5 | xhigh | S20 (fresh) |  |

## Session rules

- Steps that depend on the same freshly written code share a session.
- Work on different tools or files gets its own session.
- Reviews and verification run in fresh sessions.
- Each session ends with a PR merged to `main`; the next starts from a clean `main`.
- After finishing a step, set its Status cell to `Done` in the same PR.

## Notes

- Windows phases (3 and 5.3) need a Windows machine or runner to build and test; the current self-hosted runner is a Mac.
