# Clipo

A menu-bar / system-tray clipboard history app for macOS and Windows with fully native UIs. Local-only, encrypted at rest, no network calls.

Status: early development (core done, macOS app in progress). See the [work plan](docs/PLAN.md) and the [build guide](BUILD-GUIDE.md).

## Layout

| Path | Contents |
|---|---|
| `core/` | Rust engine (`clipo-core`): model, encrypted store, history rules and search. `core/ffi` exports it to Swift and C# with UniFFI; `core/xtask` builds the bindings and packages |
| `macos/` | Swift app (`Clipo.xcodeproj`): menu-bar shell linked to the core |
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

## Building the macOS app

Needs macOS 14+ and Xcode 16+. Package the core first; the project links it as a local Swift package:

```sh
(cd core && cargo xtask package macos)
open macos/Clipo.xcodeproj           # or, from macos/:
xcodebuild test -project Clipo.xcodeproj -scheme Clipo -destination 'platform=macOS'
```

Run `cargo xtask package macos` again after changing the core. Lint with `swiftlint lint --strict` from `macos/`. See [decision 0009](docs/decisions/0009-macos-app-shell.md) for how the app stores its key.

## Requirements

macOS 14+ or Windows 11. Builds are unsigned for now.
