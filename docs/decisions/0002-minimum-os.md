# 0002 — Minimum OS: macOS 14, Windows 11

Status: accepted

**Decision.** Support macOS 14 (Sonoma) and later, and Windows 11 only.

**Why.** Newer SwiftUI and AppKit APIs on macOS 14. WinUI 3 targets Windows 11 cleanly, and dropping Windows 10 shrinks the test matrix.

**Consequences.** The build guide's Phase 3 "done" line mentions Windows 10 and 11. It is narrowed to Windows 11. Windows 10 users are not supported.
