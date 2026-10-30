//! Release packages of the core for the apps, in `core/dist/`.

use std::process::Command;

use anyhow::{bail, Context, Result};
use camino::{Utf8Path, Utf8PathBuf};

use crate::bindings::{self, CSHARP_SOURCE, SWIFT_FFI_MODULE, SWIFT_SOURCE};
use crate::build::{self, Profile};
use crate::{core_dir, fresh_dir};

const MACOS_TARGETS: [&str; 2] = ["aarch64-apple-darwin", "x86_64-apple-darwin"];
const WINDOWS_TARGETS: [&str; 2] = ["x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc"];

/// Where `package macos` puts the Swift package.
pub(crate) fn swift_package_dir() -> Utf8PathBuf {
    core_dir().join("dist/apple/ClipoCore")
}

/// `cargo xtask package macos`: a Swift package with an XCFramework holding
/// one universal static library.
///
/// ```text
/// dist/apple/ClipoCore/
///   Package.swift
///   ClipoCoreFFI.xcframework/     static library, C header, module map
///   Sources/ClipoCore/ClipoCore.swift
/// ```
pub(crate) fn macos(targets: &[String]) -> Result<()> {
    if !cfg!(target_os = "macos") {
        bail!("package macos needs macOS with Xcode (lipo, xcodebuild)");
    }
    let targets = or_default(targets, &MACOS_TARGETS);
    let work = build::target_dir()?.join("xtask/apple");
    fresh_dir(&work)?;

    let mut staticlibs = Vec::new();
    let mut dylibs = Vec::new();
    for target in &targets {
        let libs = build::ffi(Some(target), Profile::Release)?;
        staticlibs.push(libs.staticlib);
        dylibs.push(libs.cdylib);
    }

    let universal = work.join("libclipo_ffi.a");
    build::run(
        Command::new("lipo")
            .arg("-create")
            .args(&staticlibs)
            .arg("-output")
            .arg(&universal),
    )?;

    // Bindings read the UniFFI metadata from a dynamic library; the app
    // links the static one.
    let generated = work.join("bindings");
    let headers = bindings::swift(&dylibs[0], &generated)?;

    let package = swift_package_dir();
    fresh_dir(&package)?;
    // -headers copies the folder's contents, so hand it the parent of
    // ClipoCoreFFI/ to keep the module map in its own folder.
    build::run(
        Command::new("xcodebuild")
            .arg("-create-xcframework")
            .arg("-library")
            .arg(&universal)
            .arg("-headers")
            .arg(headers.parent().context("header folder has a parent")?)
            .arg("-output")
            .arg(package.join(format!("{SWIFT_FFI_MODULE}.xcframework"))),
    )?;
    // Only the C module belongs in the XCFramework, not the Swift source.
    let sources = package.join("Sources/ClipoCore");
    std::fs::create_dir_all(&sources)?;
    copy(&generated.join(SWIFT_SOURCE), &sources.join(SWIFT_SOURCE))?;
    copy(
        &core_dir().join("ffi/swift/Package.swift"),
        &package.join("Package.swift"),
    )?;
    println!("Swift package written to {package}");
    Ok(())
}

/// `cargo xtask package windows`: one DLL per architecture in the .NET
/// runtime-identifier layout, plus the C# bindings.
///
/// ```text
/// dist/windows/
///   ClipoCore.cs
///   runtimes/win-x64/native/clipo_ffi.dll
///   runtimes/win-arm64/native/clipo_ffi.dll
/// ```
pub(crate) fn windows(targets: &[String]) -> Result<()> {
    if !cfg!(windows) {
        bail!("package windows needs Windows with the MSVC build tools");
    }
    let targets = or_default(targets, &WINDOWS_TARGETS);
    let out = core_dir().join("dist/windows");
    fresh_dir(&out)?;

    let mut dlls = Vec::new();
    for target in &targets {
        let dll = build::ffi(Some(target), Profile::Release)?.cdylib;
        let native = out.join(format!("runtimes/{}/native", runtime_id(target)?));
        std::fs::create_dir_all(&native)?;
        copy(&dll, &native.join("clipo_ffi.dll"))?;
        dlls.push(dll);
    }

    let generated = build::target_dir()?.join("xtask/csharp");
    fresh_dir(&generated)?;
    let source = bindings::csharp(&dlls[0], &generated)?;
    copy(&source, &out.join(CSHARP_SOURCE))?;
    println!("Windows package written to {out}");
    Ok(())
}

/// The .NET runtime identifier for a Windows target triple.
fn runtime_id(target: &str) -> Result<&'static str> {
    Ok(match target {
        "x86_64-pc-windows-msvc" => "win-x64",
        "aarch64-pc-windows-msvc" => "win-arm64",
        "i686-pc-windows-msvc" => "win-x86",
        _ => bail!("no .NET runtime identifier for {target}"),
    })
}

fn or_default(targets: &[String], default: &[&str]) -> Vec<String> {
    if targets.is_empty() {
        default.iter().map(|&t| t.to_owned()).collect()
    } else {
        targets.to_vec()
    }
}

fn copy(from: &Utf8Path, to: &Utf8Path) -> Result<()> {
    std::fs::copy(from, to).with_context(|| format!("copying {from} to {to}"))?;
    Ok(())
}
