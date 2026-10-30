//! Swift and C# binding generation from a built `clipo-ffi` library.
//!
//! Names come from `ffi/uniffi.toml`: Swift module `ClipoCore` over the C
//! module `ClipoCoreFFI`, C# namespace `Clipo.Core`.

use anyhow::{Context, Result};
use camino::{Utf8Path, Utf8PathBuf};
use uniffi_bindgen::bindings::{generate_swift_bindings, SwiftBindingsOptions};

use crate::build::{self, Profile};
use crate::{core_dir, fresh_dir};

/// Name of the C module; also the folder its header and module map go in.
pub(crate) const SWIFT_FFI_MODULE: &str = "ClipoCoreFFI";
/// Generated Swift source.
pub(crate) const SWIFT_SOURCE: &str = "ClipoCore.swift";
/// Generated C# source.
pub(crate) const CSHARP_SOURCE: &str = "ClipoCore.cs";

/// `cargo xtask bindings`: both languages into `core/generated/`.
pub(crate) fn generate_all() -> Result<()> {
    let libs = build::ffi(None, Profile::Debug)?;
    let out = core_dir().join("generated");
    swift(&libs.cdylib, &out.join("swift"))?;
    csharp(&libs.cdylib, &out.join("csharp"))?;
    println!("Bindings written to {out}");
    Ok(())
}

/// Writes `ClipoCore.swift`, and `ClipoCoreFFI/` with the C header and
/// module map, into `out`. Returns the path of the header folder.
///
/// The module map sits in a folder named after the module, so it cannot
/// collide with another XCFramework's `module.modulemap`, and it links
/// Security.framework, which SQLCipher's CommonCrypto backend needs.
pub(crate) fn swift(library: &Utf8Path, out: &Utf8Path) -> Result<Utf8PathBuf> {
    fresh_dir(out)?;
    let headers = out.join(SWIFT_FFI_MODULE);
    std::fs::create_dir_all(&headers)?;
    let options = |sources: bool, out_dir: &Utf8Path| SwiftBindingsOptions {
        generate_swift_sources: sources,
        generate_headers: !sources,
        generate_modulemap: !sources,
        source: library.to_owned(),
        out_dir: out_dir.to_owned(),
        xcframework: false,
        module_name: Some(SWIFT_FFI_MODULE.into()),
        modulemap_filename: Some("module.modulemap".into()),
        metadata_no_deps: false,
        link_frameworks: vec!["Security".into()],
    };
    generate_swift_bindings(options(true, out)).context("generating Swift bindings")?;
    generate_swift_bindings(options(false, &headers)).context("generating the Swift C module")?;
    require(&out.join(SWIFT_SOURCE))?;
    Ok(headers)
}

/// Writes `ClipoCore.cs` into `out`.
pub(crate) fn csharp(library: &Utf8Path, out: &Utf8Path) -> Result<Utf8PathBuf> {
    fresh_dir(out)?;
    build::run(build::cargo().args([
        "run",
        "--quiet",
        "--package",
        "xtask",
        "--bin",
        "uniffi-bindgen-cs",
        "--",
        "--library",
        library.as_str(),
        "--out-dir",
        out.as_str(),
        "--no-format",
    ]))
    .context("generating C# bindings")?;
    // The generator names the file after the UniFFI namespace.
    let file = out.join(CSHARP_SOURCE);
    std::fs::rename(out.join("clipo.cs"), &file).context("renaming clipo.cs")?;
    Ok(file)
}

fn require(file: &Utf8Path) -> Result<()> {
    if file.is_file() {
        Ok(())
    } else {
        anyhow::bail!("expected {file} to be generated")
    }
}
