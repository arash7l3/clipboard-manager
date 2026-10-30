# Clipo

A menu-bar / system-tray clipboard history app for macOS and Windows with fully native UIs. Local-only, encrypted at rest, no network calls.

Status: early development (Phase 0 done, Phase 1 in progress). See the [work plan](docs/PLAN.md) and the [build guide](BUILD-GUIDE.md).

## Layout

| Path | Contents |
|---|---|
| `core/` | Rust engine (`clipo-core`): data model and encrypted store done; dedup, search, ignore rules planned |
| `macos/` | Swift app, SwiftUI + AppKit (planned) |
| `windows/` | C# app, WinUI 3 (planned) |
| `docs/` | Work plan and [decision records](docs/decisions/README.md) |
| `assets/` | Icon source |

## Requirements

macOS 14+ or Windows 11. Builds are unsigned for now.
