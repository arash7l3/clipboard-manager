//! FFI mirrors of the core's model and store types, with conversions.
//!
//! Content-bearing types do not implement `Debug` (see the crate docs).

use std::time::{Duration, SystemTime};

use clipo_core::Timestamp;

/// Saturating `usize` to `u64`; never loses anything on supported targets.
fn to_u64(n: usize) -> u64 {
    u64::try_from(n).unwrap_or(u64::MAX)
}

/// Saturating `u64` to `usize`: a limit past `usize::MAX` means "no limit".
fn to_usize(n: u64) -> usize {
    usize::try_from(n).unwrap_or(usize::MAX)
}

/// The app a capture came from: a bundle identifier on macOS, an executable
/// name or AUMID on Windows.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct SourceApp {
    /// Stable identifier, matched against the ignore list.
    pub id: String,
    /// Display name, if known.
    #[uniffi(default = None)]
    pub name: Option<String>,
}

/// Text and an optional rich representation of the same text.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct TextContent {
    /// Plain-text form; never empty. Used for search and dedup.
    pub plain: String,
    /// RTF or HTML form, restored alongside the plain text.
    #[uniffi(default = None)]
    pub rich: Option<RichText>,
}

/// A rich-text representation.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct RichText {
    /// Markup language of `data`.
    pub format: RichTextFormat,
    /// The markup itself.
    pub data: String,
}

/// Rich-text markup languages.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum RichTextFormat {
    /// Rich Text Format.
    Rtf,
    /// HTML fragment.
    Html,
}

/// An image and its thumbnail.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct ImageContent {
    /// Encoding of `data`.
    pub format: ImageFormat,
    /// Width in pixels.
    pub width: u32,
    /// Height in pixels.
    pub height: u32,
    /// The encoded image.
    pub data: Vec<u8>,
    /// Small PNG preview made by the app, if any.
    #[uniffi(default = None)]
    pub thumbnail: Option<Vec<u8>>,
}

/// Image encodings.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum ImageFormat {
    /// PNG.
    Png,
    /// TIFF (the macOS pasteboard's native format).
    Tiff,
    /// JPEG.
    Jpeg,
    /// BMP / DIB (the Windows clipboard's native format).
    Bmp,
}

/// A file or folder reference. Clipo stores the path, not the file.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct FileRef {
    /// Absolute path; never empty.
    pub path: String,
    /// Size at capture time, if known.
    #[uniffi(default = None)]
    pub size_bytes: Option<u64>,
}

/// What was copied.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum ItemContent {
    /// Text, possibly with a rich form.
    Text {
        /// The text.
        text: TextContent,
    },
    /// An image.
    Image {
        /// The image.
        image: ImageContent,
    },
    /// One or more files.
    Files {
        /// The files, in clipboard order; never empty.
        files: Vec<FileRef>,
    },
}

/// One clipboard change, as handed to [`ClipStore::record`](crate::ClipStore::record).
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct Capture {
    /// What was copied.
    pub content: ItemContent,
    /// Where it was copied from, if known.
    #[uniffi(default = None)]
    pub source_app: Option<SourceApp>,
    /// When the watcher saw the change.
    pub captured_at: SystemTime,
    /// The source marked the data as concealed or transient
    /// (`org.nspasteboard.ConcealedType`, `ExcludeClipboardContentFromMonitorProcessing`).
    #[uniffi(default = false)]
    pub concealed: bool,
}

/// A stored history item.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct ClipItem {
    /// Store-assigned id, stable for the item's lifetime.
    pub id: i64,
    /// What was copied.
    pub content: ItemContent,
    /// Pinned items are kept past the history limit and auto-clear.
    pub pinned: bool,
    /// Where it was first copied from, if known.
    pub source_app: Option<SourceApp>,
    /// When the content was first copied.
    pub created_at: SystemTime,
    /// When the content was last copied. History is sorted by when copies
    /// were recorded, newest first, even if the clock went back.
    pub last_copied_at: SystemTime,
}

/// What a text item looks like, for its icon and screen-reader label.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum TextHint {
    /// Ordinary text.
    Plain,
    /// A single web, mail or file link and nothing else.
    Link,
    /// Several lines that look like source code.
    Code,
}

/// The part of an item's content a popup row shows.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum PreviewContent {
    /// The start of the item's plain text.
    Text {
        /// The first characters of the text (1000 at most).
        snippet: String,
        /// The text goes on past `snippet`.
        truncated: bool,
        /// What the text looks like.
        hint: TextHint,
    },
    /// An image's size and thumbnail.
    Image {
        /// Encoding of the full image.
        format: ImageFormat,
        /// Width in pixels.
        width: u32,
        /// Height in pixels.
        height: u32,
        /// Small PNG preview made by the app, if any.
        thumbnail: Option<Vec<u8>>,
    },
    /// The first files of the item.
    Files {
        /// The first files (10 at most), in clipboard order; never empty.
        files: Vec<FileRef>,
        /// Number of files in the item.
        total: u64,
    },
}

/// A history item as the popup lists it: long text cut, images as their
/// thumbnail, long file lists cut. Load the full item with
/// [`ClipStore::get`](crate::ClipStore::get) when it is picked.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct ItemPreview {
    /// Store-assigned id, stable for the item's lifetime.
    pub id: i64,
    /// What the row shows.
    pub content: PreviewContent,
    /// Pinned items are kept past the history limit and auto-clear.
    pub pinned: bool,
    /// Where it was first copied from, if known.
    pub source_app: Option<SourceApp>,
    /// When the content was last copied.
    pub last_copied_at: SystemTime,
}

/// A saved text template, kept apart from the history: never evicted, never
/// expired. No `Debug`: snippets are often credentials or addresses.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct Snippet {
    /// Store-assigned id, stable for the snippet's lifetime.
    pub id: i64,
    /// Short name shown in lists and matched by search; not blank.
    pub title: String,
    /// The text that is pasted; not empty.
    pub body: String,
    /// When the snippet was created.
    pub created_at: SystemTime,
    /// When the title or body last changed.
    pub updated_at: SystemTime,
}

/// What [`ClipStore::import_data`](crate::ClipStore::import_data) did.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Record)]
pub struct ImportSummary {
    /// History items added.
    pub items_added: u64,
    /// History items skipped because the same content was already stored.
    pub items_duplicate: u64,
    /// History items skipped for being over the item size limit.
    pub items_too_large: u64,
    /// Snippets added.
    pub snippets_added: u64,
    /// Snippets skipped because one with the same title and body exists.
    pub snippets_duplicate: u64,
}

/// The popup tabs.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum Category {
    /// Every item.
    All,
    /// Text items.
    Text,
    /// Image items.
    Images,
    /// File items.
    Files,
    /// Pinned items of any kind.
    Pinned,
}

/// A tab and search text, as typed in the popup. No `Debug`: the search text
/// is usually part of something copied.
#[derive(Clone, PartialEq, Eq, uniffi::Record)]
pub struct Query {
    /// The tab to list.
    pub category: Category,
    /// Words that must all appear in an item's text or file paths, ignoring
    /// case and Persian/Arabic letter variants. Empty lists the whole tab.
    #[uniffi(default = "")]
    pub text: String,
    /// At most this many results, newest first.
    #[uniffi(default = None)]
    pub limit: Option<u64>,
}

/// History size limits.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Record)]
pub struct HistoryLimits {
    /// Unpinned items kept; the oldest are evicted past this. At least 1.
    pub max_items: u64,
    /// Larger captures are skipped. At least 1.
    pub max_item_bytes: u64,
}

/// What is captured and how long it is kept.
#[derive(Debug, Clone, PartialEq, Eq, uniffi::Record)]
pub struct CaptureRules {
    /// Capture nothing while on.
    #[uniffi(default = false)]
    pub private_mode: bool,
    /// Source app ids never captured, compared case-insensitively.
    #[uniffi(default = [])]
    pub ignored_apps: Vec<String>,
    /// Unpinned items older than this are deleted by
    /// [`ClipStore::clear_expired`](crate::ClipStore::clear_expired).
    /// `None` keeps them; zero is rejected.
    #[uniffi(default = None)]
    pub auto_clear_after: Option<Duration>,
}

/// Why a capture was not stored.
#[derive(Debug, Clone, Copy, PartialEq, Eq, uniffi::Enum)]
pub enum SkipReason {
    /// Private mode is on.
    PrivateMode,
    /// The source marked the data as concealed or transient.
    Concealed,
    /// The source app is on the ignore list.
    IgnoredApp,
    /// The content is larger than [`HistoryLimits::max_item_bytes`].
    TooLarge,
}

/// What [`ClipStore::record`](crate::ClipStore::record) did with a capture.
#[derive(Clone, PartialEq, Eq, uniffi::Enum)]
pub enum Recorded {
    /// New content, stored as a new item.
    Added {
        /// The new item.
        item: ClipItem,
    },
    /// Content already in history; that item moved to the top.
    Bumped {
        /// The existing item, with its new copy time.
        item: ClipItem,
    },
    /// Nothing was stored.
    Skipped {
        /// Why.
        reason: SkipReason,
    },
}

// Core to FFI.

impl From<clipo_core::SourceApp> for SourceApp {
    fn from(app: clipo_core::SourceApp) -> Self {
        Self {
            id: app.id,
            name: app.name,
        }
    }
}

impl From<clipo_core::RichTextFormat> for RichTextFormat {
    fn from(format: clipo_core::RichTextFormat) -> Self {
        match format {
            clipo_core::RichTextFormat::Rtf => Self::Rtf,
            clipo_core::RichTextFormat::Html => Self::Html,
        }
    }
}

impl From<clipo_core::ImageFormat> for ImageFormat {
    fn from(format: clipo_core::ImageFormat) -> Self {
        match format {
            clipo_core::ImageFormat::Png => Self::Png,
            clipo_core::ImageFormat::Tiff => Self::Tiff,
            clipo_core::ImageFormat::Jpeg => Self::Jpeg,
            clipo_core::ImageFormat::Bmp => Self::Bmp,
        }
    }
}

impl From<clipo_core::FileRef> for FileRef {
    fn from(file: clipo_core::FileRef) -> Self {
        Self {
            path: file.path,
            size_bytes: file.size_bytes,
        }
    }
}

impl From<clipo_core::ItemContent> for ItemContent {
    fn from(content: clipo_core::ItemContent) -> Self {
        match content {
            clipo_core::ItemContent::Text(text) => Self::Text {
                text: TextContent {
                    plain: text.plain,
                    rich: text.rich.map(|rich| RichText {
                        format: rich.format.into(),
                        data: rich.data,
                    }),
                },
            },
            clipo_core::ItemContent::Image(image) => Self::Image {
                image: ImageContent {
                    format: image.format.into(),
                    width: image.width,
                    height: image.height,
                    data: image.data,
                    thumbnail: image.thumbnail,
                },
            },
            clipo_core::ItemContent::Files(files) => Self::Files {
                files: files.into_iter().map(FileRef::from).collect(),
            },
        }
    }
}

impl From<clipo_core::ClipItem> for ClipItem {
    fn from(item: clipo_core::ClipItem) -> Self {
        Self {
            id: item.id.as_raw(),
            content: item.content.into(),
            pinned: item.pinned,
            source_app: item.source_app.map(SourceApp::from),
            created_at: item.created_at.to_system_time(),
            last_copied_at: item.last_copied_at.to_system_time(),
        }
    }
}

impl From<clipo_core::TextHint> for TextHint {
    fn from(hint: clipo_core::TextHint) -> Self {
        match hint {
            clipo_core::TextHint::Plain => Self::Plain,
            clipo_core::TextHint::Link => Self::Link,
            clipo_core::TextHint::Code => Self::Code,
        }
    }
}

impl From<clipo_core::PreviewContent> for PreviewContent {
    fn from(content: clipo_core::PreviewContent) -> Self {
        match content {
            clipo_core::PreviewContent::Text {
                snippet,
                truncated,
                hint,
            } => Self::Text {
                snippet,
                truncated,
                hint: hint.into(),
            },
            clipo_core::PreviewContent::Image {
                format,
                width,
                height,
                thumbnail,
            } => Self::Image {
                format: format.into(),
                width,
                height,
                thumbnail,
            },
            clipo_core::PreviewContent::Files { files, total } => Self::Files {
                files: files.into_iter().map(FileRef::from).collect(),
                total: to_u64(total),
            },
        }
    }
}

impl From<clipo_core::ItemPreview> for ItemPreview {
    fn from(preview: clipo_core::ItemPreview) -> Self {
        Self {
            id: preview.id.as_raw(),
            content: preview.content.into(),
            pinned: preview.pinned,
            source_app: preview.source_app.map(SourceApp::from),
            last_copied_at: preview.last_copied_at.to_system_time(),
        }
    }
}

impl From<clipo_core::Snippet> for Snippet {
    fn from(snippet: clipo_core::Snippet) -> Self {
        Self {
            id: snippet.id.as_raw(),
            title: snippet.title,
            body: snippet.body,
            created_at: snippet.created_at.to_system_time(),
            updated_at: snippet.updated_at.to_system_time(),
        }
    }
}

impl From<clipo_core::ImportSummary> for ImportSummary {
    fn from(summary: clipo_core::ImportSummary) -> Self {
        Self {
            items_added: to_u64(summary.items_added),
            items_duplicate: to_u64(summary.items_duplicate),
            items_too_large: to_u64(summary.items_too_large),
            snippets_added: to_u64(summary.snippets_added),
            snippets_duplicate: to_u64(summary.snippets_duplicate),
        }
    }
}

impl From<clipo_core::Category> for Category {
    fn from(category: clipo_core::Category) -> Self {
        match category {
            clipo_core::Category::All => Self::All,
            clipo_core::Category::Text => Self::Text,
            clipo_core::Category::Images => Self::Images,
            clipo_core::Category::Files => Self::Files,
            clipo_core::Category::Pinned => Self::Pinned,
        }
    }
}

impl From<clipo_core::HistoryLimits> for HistoryLimits {
    fn from(limits: clipo_core::HistoryLimits) -> Self {
        Self {
            max_items: to_u64(limits.max_items),
            max_item_bytes: to_u64(limits.max_item_bytes),
        }
    }
}

impl From<clipo_core::CaptureRules> for CaptureRules {
    fn from(rules: clipo_core::CaptureRules) -> Self {
        Self {
            private_mode: rules.private_mode,
            ignored_apps: rules.ignored_apps,
            auto_clear_after: rules.auto_clear_after,
        }
    }
}

impl From<clipo_core::SkipReason> for SkipReason {
    fn from(reason: clipo_core::SkipReason) -> Self {
        match reason {
            clipo_core::SkipReason::PrivateMode => Self::PrivateMode,
            clipo_core::SkipReason::Concealed => Self::Concealed,
            clipo_core::SkipReason::IgnoredApp => Self::IgnoredApp,
            clipo_core::SkipReason::TooLarge => Self::TooLarge,
        }
    }
}

impl From<clipo_core::Recorded> for Recorded {
    fn from(recorded: clipo_core::Recorded) -> Self {
        match recorded {
            clipo_core::Recorded::Added(item) => Self::Added { item: item.into() },
            clipo_core::Recorded::Bumped(item) => Self::Bumped { item: item.into() },
            clipo_core::Recorded::Skipped(reason) => Self::Skipped {
                reason: reason.into(),
            },
        }
    }
}

// FFI to core.

impl From<SourceApp> for clipo_core::SourceApp {
    fn from(app: SourceApp) -> Self {
        Self {
            id: app.id,
            name: app.name,
        }
    }
}

impl From<RichTextFormat> for clipo_core::RichTextFormat {
    fn from(format: RichTextFormat) -> Self {
        match format {
            RichTextFormat::Rtf => Self::Rtf,
            RichTextFormat::Html => Self::Html,
        }
    }
}

impl From<ImageFormat> for clipo_core::ImageFormat {
    fn from(format: ImageFormat) -> Self {
        match format {
            ImageFormat::Png => Self::Png,
            ImageFormat::Tiff => Self::Tiff,
            ImageFormat::Jpeg => Self::Jpeg,
            ImageFormat::Bmp => Self::Bmp,
        }
    }
}

impl From<FileRef> for clipo_core::FileRef {
    fn from(file: FileRef) -> Self {
        Self {
            path: file.path,
            size_bytes: file.size_bytes,
        }
    }
}

impl From<ItemContent> for clipo_core::ItemContent {
    fn from(content: ItemContent) -> Self {
        match content {
            ItemContent::Text { text } => Self::Text(clipo_core::TextContent {
                plain: text.plain,
                rich: text.rich.map(|rich| clipo_core::RichText {
                    format: rich.format.into(),
                    data: rich.data,
                }),
            }),
            ItemContent::Image { image } => Self::Image(clipo_core::ImageContent {
                format: image.format.into(),
                width: image.width,
                height: image.height,
                data: image.data,
                thumbnail: image.thumbnail,
            }),
            ItemContent::Files { files } => {
                Self::Files(files.into_iter().map(clipo_core::FileRef::from).collect())
            }
        }
    }
}

impl From<Capture> for clipo_core::Capture {
    fn from(capture: Capture) -> Self {
        Self {
            content: capture.content.into(),
            source_app: capture.source_app.map(clipo_core::SourceApp::from),
            captured_at: Timestamp::from(capture.captured_at),
            concealed: capture.concealed,
        }
    }
}

impl From<Category> for clipo_core::Category {
    fn from(category: Category) -> Self {
        match category {
            Category::All => Self::All,
            Category::Text => Self::Text,
            Category::Images => Self::Images,
            Category::Files => Self::Files,
            Category::Pinned => Self::Pinned,
        }
    }
}

impl From<Query> for clipo_core::Query {
    fn from(query: Query) -> Self {
        Self {
            category: query.category.into(),
            text: query.text,
            limit: query.limit.map(to_usize),
        }
    }
}

impl From<HistoryLimits> for clipo_core::HistoryLimits {
    fn from(limits: HistoryLimits) -> Self {
        Self {
            max_items: to_usize(limits.max_items),
            max_item_bytes: to_usize(limits.max_item_bytes),
        }
    }
}

impl From<CaptureRules> for clipo_core::CaptureRules {
    fn from(rules: CaptureRules) -> Self {
        Self {
            private_mode: rules.private_mode,
            ignored_apps: rules.ignored_apps,
            auto_clear_after: rules.auto_clear_after,
        }
    }
}
