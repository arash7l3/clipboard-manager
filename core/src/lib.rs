//! Clipo core: the shared, UI-free engine behind the macOS and Windows apps.
//!
//! The platform apps capture clipboard changes and hand them to this crate as
//! [`Capture`] values; the core owns history, rules and storage. History lives
//! in an encrypted [`Store`] whose key the apps keep in the OS credential store
//! through [`KeyStore`].
//!
//! Clipboard contents are sensitive. Types that carry them implement `Debug`
//! by hand so that formatting an item never prints its text, bytes or paths,
//! and error messages never quote them.

pub mod error;
pub mod key;
pub mod model;
pub mod store;

pub use error::{CoreError, InvalidContent, InvalidSnippet};
pub use key::{DatabaseKey, KeyStore, KeyStoreError, KEY_LEN};
pub use model::{
    Capture, Category, ClipItem, ContentHash, FileRef, ImageContent, ImageFormat, ItemContent,
    ItemId, ItemKind, ItemPreview, PreviewContent, RichText, RichTextFormat, Snippet, SnippetId,
    SourceApp, TextContent, TextHint, Timestamp,
};
pub use store::{CaptureRules, HistoryLimits, ImportSummary, Query, Recorded, SkipReason, Store};
