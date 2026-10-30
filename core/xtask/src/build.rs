//! Building `clipo-ffi` and running other tools.

use std::io::BufReader;
use std::process::{Command, Stdio};
use std::sync::OnceLock;

use anyhow::{bail, Context, Result};
use camino::Utf8PathBuf;
use cargo_metadata::Message;

/// The libraries one `cargo build` of `clipo-ffi` produced.
pub(crate) struct FfiLibs {
    /// `libclipo_ffi.a`, or `clipo_ffi.lib` on Windows.
    pub(crate) staticlib: Utf8PathBuf,
    /// `libclipo_ffi.so`, `libclipo_ffi.dylib` or `clipo_ffi.dll`.
    pub(crate) cdylib: Utf8PathBuf,
}

/// Release builds for packaging; debug builds for bindings and smoke tests.
#[derive(Clone, Copy, PartialEq, Eq)]
pub(crate) enum Profile {
    Debug,
    Release,
}

/// Builds `clipo-ffi` for `target` (the host if `None`) and returns where
/// the libraries are.
pub(crate) fn ffi(target: Option<&str>, profile: Profile) -> Result<FfiLibs> {
    let mut cmd = cargo();
    cmd.args([
        "build",
        "--package",
        "clipo-ffi",
        "--message-format=json-render-diagnostics",
    ]);
    if profile == Profile::Release {
        cmd.arg("--release");
    }
    if let Some(target) = target {
        cmd.args(["--target", target]);
    }
    println!(
        "Building clipo-ffi for {}",
        target.unwrap_or("this machine")
    );

    let mut child = cmd.stdout(Stdio::piped()).spawn()?;
    let stdout = child.stdout.take().context("no cargo stdout")?;
    let mut staticlib = None;
    let mut cdylib = None;
    for message in Message::parse_stream(BufReader::new(stdout)) {
        let Message::CompilerArtifact(artifact) = message? else {
            continue;
        };
        if artifact.target.name != "clipo_ffi" {
            continue;
        }
        for file in artifact.filenames {
            let name = file.file_name().unwrap_or_default();
            match file.extension() {
                Some("a") => staticlib = Some(file),
                // MSVC also writes clipo_ffi.dll.lib, the DLL's import library.
                Some("lib") if !name.ends_with(".dll.lib") => staticlib = Some(file),
                Some("so" | "dylib" | "dll") => cdylib = Some(file),
                _ => {}
            }
        }
    }
    if !child.wait()?.success() {
        bail!("cargo build failed");
    }
    Ok(FfiLibs {
        staticlib: staticlib.context("cargo did not report the static library")?,
        cdylib: cdylib.context("cargo did not report the dynamic library")?,
    })
}

/// The cargo running this task, or `cargo` from `PATH`.
pub(crate) fn cargo() -> Command {
    Command::new(std::env::var_os("CARGO").unwrap_or_else(|| "cargo".into()))
}

/// Runs `cmd`, failing if it cannot start or exits unsuccessfully.
pub(crate) fn run(cmd: &mut Command) -> Result<()> {
    let program = cmd.get_program().to_string_lossy().into_owned();
    let status = cmd
        .status()
        .with_context(|| format!("could not run {program}"))?;
    if !status.success() {
        bail!("{program} failed ({status})");
    }
    Ok(())
}

/// `cargo metadata`'s target directory (honours `CARGO_TARGET_DIR`).
pub(crate) fn target_dir() -> Result<Utf8PathBuf> {
    static DIR: OnceLock<Utf8PathBuf> = OnceLock::new();
    if let Some(dir) = DIR.get() {
        return Ok(dir.clone());
    }
    let metadata = cargo_metadata::MetadataCommand::new().no_deps().exec()?;
    Ok(DIR.get_or_init(|| metadata.target_directory).clone())
}
