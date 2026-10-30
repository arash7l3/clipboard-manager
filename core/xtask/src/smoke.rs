//! End-to-end checks that the generated bindings load the library and work:
//! small Swift and C# programs in `ffi/smoke/` open a store, record and search.

use std::process::Command;

use anyhow::{Context, Result};

use crate::bindings;
use crate::build::{self, Profile};
use crate::core_dir;
use crate::package;

/// `cargo xtask smoke swift`: packages for macOS, then `swift run`s
/// `ffi/smoke/swift` against the package.
pub(crate) fn swift() -> Result<()> {
    package::macos(&[])?;
    let scratch = build::target_dir()?.join("xtask/swift-smoke");
    build::run(
        Command::new("swift")
            .arg("run")
            .arg("--package-path")
            .arg(core_dir().join("ffi/smoke/swift"))
            .arg("--scratch-path")
            .arg(&scratch),
    )
    .context("Swift smoke test")
}

/// `cargo xtask smoke csharp`: generates the C# bindings from a debug build
/// for this machine, then builds and runs `ffi/smoke/csharp` with the library.
pub(crate) fn csharp() -> Result<()> {
    let libs = build::ffi(None, Profile::Debug)?;
    bindings::csharp(&libs.cdylib, &core_dir().join("generated/csharp"))?;
    let artifacts = build::target_dir()?.join("xtask/csharp-smoke");
    // Built and run in two steps: `dotnet run` ignores --artifacts-path when
    // looking for the program on .NET 8.
    build::run(
        Command::new("dotnet")
            .arg("build")
            .arg(core_dir().join("ffi/smoke/csharp"))
            .arg("--artifacts-path")
            .arg(&artifacts)
            // Read by ClipoSmoke.csproj, which copies it next to the program.
            .env("CLIPO_NATIVE_LIBRARY", &libs.cdylib),
    )
    .context("building the C# smoke test")?;
    build::run(Command::new("dotnet").arg(artifacts.join("bin/ClipoSmoke/debug/ClipoSmoke.dll")))
        .context("C# smoke test")
}
