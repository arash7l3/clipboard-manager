# Clipo

A menu-bar / system-tray clipboard history app for macOS and Windows with fully native UIs. Local-only, encrypted at rest, no network calls.

Status: early development (Phase 0 done, Phase 1 in progress). See the [work plan](docs/PLAN.md) and the [build guide](BUILD-GUIDE.md).

## Layout

| Path | Contents |
|---|---|
| `core/` | Rust engine (`clipo-core`): model, encrypted store, history rules and search. `core/ffi` exports it to Swift and C# with UniFFI; `core/xtask` builds the bindings and packages |
| `macos/` | Swift app, SwiftUI + AppKit (planned) |
| `windows/` | C# app, WinUI 3 (planned) |
| `docs/` | Work plan and [decision records](docs/decisions/README.md) |
| `assets/` | Icon source |

## Building the core

From `core/`:

```sh
cargo test --workspace         # core and FFI tests
cargo xtask bindings           # Swift and C# bindings into core/generated/
cargo xtask package macos      # Swift package into core/dist/apple/ (macOS + Xcode)
cargo xtask package windows    # DLLs and C# bindings into core/dist/windows/ (Windows + MSVC)
```

See [decision 0008](docs/decisions/0008-bindings-and-packaging.md) for what each package contains.

## Requirements

macOS 14+ or Windows 11. Builds are unsigned for now.
