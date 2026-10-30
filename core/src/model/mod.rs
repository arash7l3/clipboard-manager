//! Data model: history items, their content and the categories shown as tabs.

mod content;
mod item;
pub(crate) mod redact;
mod time;

pub use content::{
    ContentHash, FileRef, ImageContent, ImageFormat, ItemContent, ItemKind, RichText,
    RichTextFormat, TextContent,
};
pub use item::{Capture, Category, ClipItem, ItemId, SourceApp};
pub use time::Timestamp;
