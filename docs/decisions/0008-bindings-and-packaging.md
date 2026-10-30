# 0008 — Bindings: UniFFI 0.31 in a separate crate, packaged by a Rust xtask

Status: accepted

**Decision.** The core is exported through a second crate, `core/ffi` (`clipo-ffi`), with UniFFI 0.31 proc macros. Swift bindings come from UniFFI itself, C# bindings from NordSecurity's `uniffi-bindgen-cs` v0.11.1, which targets UniFFI 0.31. A Rust `xtask` crate (`core/xtask`) generates both and builds the packages:

| Command (from `core/`) | Output |
|---|---|
| `cargo xtask bindings` | `core/generated/swift`, `core/generated/csharp` |
| `cargo xtask package macos` | `core/dist/apple/ClipoCore`: a Swift package with a universal (arm64 + x86_64) static library in `ClipoCoreFFI.xcframework` |
| `cargo xtask package windows` | `core/dist/windows`: `ClipoCore.cs` and `runtimes/win-{x64,arm64}/native/clipo_ffi.dll` |
| `cargo xtask smoke swift` / `csharp` | Packages, then runs a small Swift or C# program against the bindings |

The apps see Swift module `ClipoCore` and C# namespace `Clipo.Core`. Generated code and packages are not committed.

**Why.**
- The core stays plain Rust: no FFI types and no generated unsafe code, so it keeps `unsafe_code = "deny"`, and its tests don't depend on UniFFI.
- The FFI crate gives FFI-friendly types (`u64` instead of `usize`, `SystemTime` that becomes `Date` / `DateTime`) and puts the store behind a mutex so the apps can call it from any thread.
- The C# generator is third-party and follows UniFFI releases with a delay. Pinning both in one `Cargo.lock` makes the scaffolding and both generators use the same UniFFI version. That is also why UniFFI is 0.31, not the newest release.
- The xtask is Rust, so the project still uses only Rust, Swift and C#. It runs the same on macOS and Windows, and nothing has to be installed beyond Rust and the platform build tools.

**Safeguards.**
- FFI types that carry clipboard content do not implement `Debug`. Error messages carry only ids, codes and SQLite messages, never content.
- The core's enums are exhaustive, so a new variant fails to compile until the FFI layer maps it.
- CI generates both bindings and runs the Swift smoke test on every push.

**Consequences.**
- Moving to a newer UniFFI waits for a matching `uniffi-bindgen-cs` release.
- The C# generator is a git dependency, so the first build needs github.com.
- `package macos` needs macOS with Xcode. `package windows` needs Windows with the MSVC build tools and Perl, because OpenSSL is compiled from source there (see 0007).
- The C# smoke test runs in CI only if the runner has `dotnet`. The Mac runner currently doesn't, so it is checked on a developer machine until the Windows job exists.
- Key bytes pass through UniFFI buffers that are freed but not wiped.
- `search` returns full items, including image data. The popup (step 2.3) may need a lighter listing that returns only thumbnails.
