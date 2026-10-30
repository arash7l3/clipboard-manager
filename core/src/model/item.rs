//! History items, incoming captures and popup categories.

use super::content::{ItemContent, ItemKind};
use super::time::Timestamp;

/// Store-assigned identifier of a history item.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct ItemId(i64);

impl ItemId {
    /// Wraps a raw identifier, for example a SQLite row id.
    #[must_use]
    pub const fn from_raw(raw: i64) -> Self {
        Self(raw)
    }

    /// The raw identifier.
    #[must_use]
    pub const fn as_raw(self) -> i64 {
        self.0
    }
}

/// The application that owned the clipboard when an item was copied.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub struct SourceApp {
    /// Stable identifier used by ignore rules: the bundle identifier on macOS,
    /// the executable path or `AppUserModelID` on Windows.
    pub id: String,
    /// Localized display name, if the OS provided one.
    pub name: Option<String>,
}

/// A clipboard change reported by a platform app, before the core stores it.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Capture {
    /// What was copied.
    pub content: ItemContent,
    /// Frontmost or owning app at copy time, if known.
    pub source_app: Option<SourceApp>,
    /// When the change was observed.
    pub captured_at: Timestamp,
    /// The source marked the data as concealed or transient (for example a
    /// password manager). The core never stores such captures.
    pub concealed: bool,
}

impl Capture {
    /// A non-concealed capture observed now.
    #[must_use]
    pub fn new(content: ItemContent, source_app: Option<SourceApp>) -> Self {
        Self {
            content,
            source_app,
            captured_at: Timestamp::now(),
            concealed: false,
        }
    }
}

/// One entry in the clipboard history.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ClipItem {
    /// Store-assigned identifier.
    pub id: ItemId,
    /// What was copied.
    pub content: ItemContent,
    /// Pinned items never expire and are never evicted by the history limit.
    pub pinned: bool,
    /// App the item was first copied from, if known.
    pub source_app: Option<SourceApp>,
    /// When the item was first captured.
    pub created_at: Timestamp,
    /// When the same content was most recently copied. History lists items
    /// in the order these copies were recorded, newest first, so a duplicate
    /// copy moves the item to the top. That order does not follow the clock:
    /// a copy recorded after the clock went back still comes first.
    pub last_copied_at: Timestamp,
}

impl ClipItem {
    /// The kind of the item's content.
    #[must_use]
    pub const fn kind(&self) -> ItemKind {
        self.content.kind()
    }
}

/// The tabs of the popup.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
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

impl Category {
    /// All categories in tab order.
    pub const ALL: [Self; 5] = [
        Self::All,
        Self::Text,
        Self::Images,
        Self::Files,
        Self::Pinned,
    ];

    /// The item kind this tab is limited to, if any.
    #[must_use]
    pub const fn kind(self) -> Option<ItemKind> {
        match self {
            Self::Text => Some(ItemKind::Text),
            Self::Images => Some(ItemKind::Image),
            Self::Files => Some(ItemKind::Files),
            Self::All | Self::Pinned => None,
        }
    }

    /// Whether `item` is listed under this tab.
    #[must_use]
    pub fn includes(self, item: &ClipItem) -> bool {
        match self {
            Self::All => true,
            Self::Pinned => item.pinned,
            Self::Text | Self::Images | Self::Files => self.kind() == Some(item.kind()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::{FileRef, ImageContent, ImageFormat, TextContent};

    fn item(content: ItemContent, pinned: bool) -> ClipItem {
        let at = Timestamp::from_millis(1);
        ClipItem {
            id: ItemId::from_raw(1),
            content,
            pinned,
            source_app: None,
            created_at: at,
            last_copied_at: at,
        }
    }

    fn samples() -> [ClipItem; 3] {
        [
            item(
                ItemContent::Text(TextContent {
                    plain: "t".into(),
                    rich: None,
                }),
                false,
            ),
            item(
                ItemContent::Image(ImageContent {
                    format: ImageFormat::Png,
                    width: 1,
                    height: 1,
                    data: vec![1],
                    thumbnail: None,
                }),
                true,
            ),
            item(
                ItemContent::Files(vec![FileRef {
                    path: "/f".into(),
                    size_bytes: None,
                }]),
                false,
            ),
        ]
    }

    fn listed(category: Category) -> Vec<ItemKind> {
        samples()
            .iter()
            .filter(|i| category.includes(i))
            .map(ClipItem::kind)
            .collect()
    }

    #[test]
    fn categories_filter_by_kind_and_pin() {
        use ItemKind::{Files, Image, Text};
        assert_eq!(listed(Category::All), [Text, Image, Files]);
        assert_eq!(listed(Category::Text), [Text]);
        assert_eq!(listed(Category::Images), [Image]);
        assert_eq!(listed(Category::Files), [Files]);
        assert_eq!(listed(Category::Pinned), [Image]);
    }

    #[test]
    fn tab_order_is_fixed() {
        assert_eq!(Category::ALL.first(), Some(&Category::All));
        assert_eq!(Category::ALL.last(), Some(&Category::Pinned));
    }

    #[test]
    fn new_capture_is_not_concealed() {
        let before = Timestamp::now();
        let capture = Capture::new(samples()[0].content.clone(), None);
        assert!(!capture.concealed);
        assert!(capture.captured_at >= before);
    }

    #[test]
    fn item_id_round_trips() {
        assert_eq!(ItemId::from_raw(42).as_raw(), 42);
    }
}
