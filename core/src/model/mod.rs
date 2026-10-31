//! Data model: history items, their content, their previews, the
//! categories shown as tabs, and snippets.

mod content;
mod item;
mod preview;
pub(crate) mod redact;
mod snippet;
mod time;

pub use content::{
    ContentHash, FileRef, ImageContent, ImageFormat, ItemContent, ItemKind, RichText,
    RichTextFormat, TextContent,
};
pub use item::{Capture, Category, ClipItem, ItemId, SourceApp};
pub use preview::{ItemPreview, PreviewContent, TextHint, PREVIEW_FILES, PREVIEW_TEXT_CHARS};
pub(crate) use snippet::validate as validate_snippet;
pub use snippet::{Snippet, SnippetId};
pub use time::Timestamp;
