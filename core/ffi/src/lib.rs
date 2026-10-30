//! UniFFI exports of [`clipo_core`] for the platform apps.
//!
//! The macOS app uses the generated Swift module `ClipoCore`; the Windows app
//! uses the generated C# namespace `Clipo.Core`. Run `cargo xtask bindings` to
//! generate both, and `cargo xtask package` to build the libraries.
//!
//! This crate only translates: every rule lives in `clipo_core`. Its types
//! mirror the core's with FFI-friendly fields (`u64` for counts, `SystemTime`
//! for timestamps, which become `Date` in Swift and `DateTime` in C#).
//!
//! Types that carry clipboard content do not implement `Debug`, so they cannot
//! end up in a log by accident.

mod error;
mod store;
mod types;

pub use error::{ClipoError, InvalidContent, KeyStoreError};
pub use store::{ClipStore, KeyStore};
pub use types::{
    Capture, CaptureRules, Category, ClipItem, FileRef, HistoryLimits, ImageContent, ImageFormat,
    ItemContent, Query, Recorded, RichText, RichTextFormat, SkipReason, SourceApp, TextContent,
};

uniffi::setup_scaffolding!("clipo");

/// The default history limits: [`clipo_core::store::DEFAULT_MAX_ITEMS`] items
/// of at most [`clipo_core::store::DEFAULT_MAX_ITEM_BYTES`] each.
#[uniffi::export]
#[must_use]
pub fn default_history_limits() -> HistoryLimits {
    clipo_core::HistoryLimits::default().into()
}

/// The popup tabs, in display order.
#[uniffi::export]
#[must_use]
pub fn tab_categories() -> Vec<Category> {
    clipo_core::Category::ALL.map(Category::from).to_vec()
}

/// Deletes the database at `path` with its WAL, shared-memory and journal
/// files. Use it to start over after [`ClipoError::KeyMissing`] or
/// [`ClipoError::WrongKey`]; all history is lost. Close every [`ClipStore`]
/// on this path first.
///
/// # Errors
///
/// [`ClipoError::Io`] if a file exists but cannot be removed.
#[uniffi::export]
pub fn delete_database(path: &str) -> Result<(), ClipoError> {
    Ok(clipo_core::Store::delete_database(std::path::Path::new(
        path,
    ))?)
}

#[cfg(test)]
mod tests;
