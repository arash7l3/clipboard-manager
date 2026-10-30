//! Clipo core: the shared, UI-free engine behind the macOS and Windows apps.
//!
//! The platform apps capture clipboard changes and hand them to this crate as
//! [`Capture`] values; the core owns history, rules and storage.
//!
//! Clipboard contents are sensitive. Types that carry them implement `Debug`
//! by hand so that formatting an item never prints its text, bytes or paths.

pub mod error;
pub mod model;

pub use error::{CoreError, InvalidContent};
pub use model::{
    Capture, Category, ClipItem, ContentHash, FileRef, ImageContent, ImageFormat, ItemContent,
    ItemId, ItemKind, RichText, RichTextFormat, SourceApp, TextContent, Timestamp,
};
