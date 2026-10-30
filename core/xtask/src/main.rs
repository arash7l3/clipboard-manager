//! Build tasks for the core: generate the UniFFI bindings, package the
//! library for each app, and smoke-test the packages from Swift and C#.
//!
//! Written in Rust so it runs the same on macOS and Windows, and so the
//! bindings generators are pinned in `Cargo.lock` with the scaffolding.

mod bindings;
mod build;
mod package;
mod smoke;

use std::process::ExitCode;

use anyhow::{bail, Result};
use camino::Utf8PathBuf;

const USAGE: &str = "\
Usage: cargo xtask <task>   (run from core/)

Tasks:
  bindings                 Generate Swift and C# bindings into core/generated/
  package macos            Build a universal static library and a Swift package
                           (core/dist/apple/ClipoCore). Needs macOS with Xcode.
  package windows          Build clipo_ffi.dll for x64 and arm64 with the C#
                           bindings (core/dist/windows). Needs Windows with MSVC.
  smoke swift              Package for macOS, then run a Swift program against it
  smoke csharp             Run a C# program against the bindings. Needs dotnet.

Options for package:
  --target <triple>        Build only this target; repeat for several
";

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(err) => {
            eprintln!("error: {err:#}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<()> {
    // The UniFFI loaders run `cargo metadata` in the working directory.
    std::env::set_current_dir(core_dir())?;

    let args: Vec<String> = std::env::args().skip(1).collect();
    let args: Vec<&str> = args.iter().map(String::as_str).collect();
    match args.as_slice() {
        ["bindings"] => bindings::generate_all(),
        ["package", "macos", rest @ ..] => package::macos(&targets(rest)?),
        ["package", "windows", rest @ ..] => package::windows(&targets(rest)?),
        ["smoke", "swift"] => smoke::swift(),
        ["smoke", "csharp"] => smoke::csharp(),
        [] | ["help" | "--help" | "-h"] => {
            print!("{USAGE}");
            Ok(())
        }
        _ => bail!("unknown task: {}\n\n{USAGE}", args.join(" ")),
    }
}

/// `--target` values, if any.
fn targets(args: &[&str]) -> Result<Vec<String>> {
    let mut targets = Vec::new();
    let mut args = args.iter();
    while let Some(arg) = args.next() {
        match (*arg, args.next()) {
            ("--target", Some(target)) => targets.push((*target).to_owned()),
            _ => bail!("unexpected argument: {arg}\n\n{USAGE}"),
        }
    }
    Ok(targets)
}

/// The `core/` directory: the workspace root.
fn core_dir() -> Utf8PathBuf {
    Utf8PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .expect("xtask lives inside core/")
        .to_owned()
}

/// Deletes and recreates an output directory. Only folders inside `core/` or
/// cargo's target directory (which `CARGO_TARGET_DIR` may move elsewhere).
fn fresh_dir(dir: &camino::Utf8Path) -> Result<()> {
    let roots = [core_dir(), build::target_dir()?];
    if !roots
        .iter()
        .any(|root| dir.starts_with(root) && dir != root)
    {
        bail!("refusing to clear {dir}: not inside core/ or the target directory");
    }
    if dir.exists() {
        std::fs::remove_dir_all(dir)?;
    }
    std::fs::create_dir_all(dir)?;
    Ok(())
}
