//! The C# bindings generator, built from the version pinned in `Cargo.lock`.
//! `cargo xtask bindings` runs it; there is nothing to install.

fn main() -> anyhow::Result<()> {
    uniffi_bindgen_cs::main()
}
