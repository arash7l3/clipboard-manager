//! What the popup needs to draw a history row, without the full content.

use std::fmt;

use super::content::{FileRef, ImageFormat, ItemKind};
use super::item::{ItemId, SourceApp};
use super::redact::Redacted;
use super::time::Timestamp;

/// Characters of an item's text kept in its preview. Enough for a few lines
/// of a row and for a long link.
pub const PREVIEW_TEXT_CHARS: usize = 1000;

/// Files of an item kept in its preview. The row shows the first one and how
/// many there are.
pub const PREVIEW_FILES: usize = 10;

/// A history item as the popup lists it. Small and cheap to load: long text is
/// cut, images come as their thumbnail only, and long file lists are cut. Load
/// the full item by id when it is picked.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ItemPreview {
    /// Store-assigned identifier.
    pub id: ItemId,
    /// What the row shows.
    pub content: PreviewContent,
    /// Pinned items never expire and are never evicted by the history limit.
    pub pinned: bool,
    /// App the item was first copied from, if known.
    pub source_app: Option<SourceApp>,
    /// When the same content was most recently copied.
    pub last_copied_at: Timestamp,
}

/// The part of an item's content a row shows.
#[derive(Clone, PartialEq, Eq)]
pub enum PreviewContent {
    /// The start of the item's plain text.
    Text {
        /// At most [`PREVIEW_TEXT_CHARS`] characters from the start.
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
        /// At most [`PREVIEW_FILES`] files, in clipboard order; never empty.
        files: Vec<FileRef>,
        /// Number of files in the item.
        total: usize,
    },
}

impl PreviewContent {
    /// The kind of the item this previews.
    #[must_use]
    pub const fn kind(&self) -> ItemKind {
        match self {
            Self::Text { .. } => ItemKind::Text,
            Self::Image { .. } => ItemKind::Image,
            Self::Files { .. } => ItemKind::Files,
        }
    }
}

/// What a text item looks like, for its icon and screen-reader label. A guess
/// from the text alone; it never changes how the item is stored or restored.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum TextHint {
    /// Ordinary text.
    Plain,
    /// A single web, mail or file link and nothing else.
    Link,
    /// Several lines that look like source code.
    Code,
}

/// Prefixes that make a single word a link. Compared ignoring ASCII case.
const LINK_PREFIXES: [&str; 6] = [
    "https://", "http://", "ftp://", "file://", "mailto:", "www.",
];

/// Line endings typical of C-like code, JSON and markup.
const CODE_LINE_ENDS: [char; 8] = ['{', '}', ';', '(', ')', '[', ']', '>'];

/// List markers that start indented prose, not code.
const BULLETS: [&str; 4] = ["- ", "* ", "• ", "+ "];

impl TextHint {
    /// Guesses from `text`. `truncated` says `text` is only the start of the
    /// item's text: a link is never cut, so a cut text is never a link.
    #[must_use]
    pub fn of(text: &str, truncated: bool) -> Self {
        if !truncated && is_link(text.trim()) {
            Self::Link
        } else if looks_like_code(text) {
            Self::Code
        } else {
            Self::Plain
        }
    }
}

fn is_link(text: &str) -> bool {
    if text.is_empty() || text.contains(char::is_whitespace) {
        return false;
    }
    LINK_PREFIXES.iter().any(|prefix| {
        text.get(..prefix.len())
            .is_some_and(|start| start.eq_ignore_ascii_case(prefix))
            && text.len() > prefix.len()
    })
}

/// At least two non-blank lines, and at least half of them indented (but not
/// as a list) or ending like a statement or block.
fn looks_like_code(text: &str) -> bool {
    let mut lines = 0_usize;
    let mut code_lines = 0_usize;
    for line in text.lines().filter(|line| !line.trim().is_empty()) {
        lines += 1;
        let body = line.trim_start();
        let indented = line.starts_with('\t') || line.starts_with("  ");
        let bullet = BULLETS.iter().any(|bullet| body.starts_with(bullet));
        let code_end = line.trim_end().ends_with(CODE_LINE_ENDS);
        if (indented && !bullet) || code_end {
            code_lines += 1;
        }
    }
    lines >= 2 && code_lines * 2 >= lines
}

impl fmt::Debug for PreviewContent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Text {
                snippet,
                truncated,
                hint,
            } => f
                .debug_struct("Text")
                .field("snippet", &Redacted(snippet.len()))
                .field("truncated", truncated)
                .field("hint", hint)
                .finish(),
            Self::Image {
                format,
                width,
                height,
                thumbnail,
            } => f
                .debug_struct("Image")
                .field("format", format)
                .field("width", width)
                .field("height", height)
                .field("thumbnail", &thumbnail.as_ref().map(|t| Redacted(t.len())))
                .finish(),
            Self::Files { files, total } => f
                .debug_struct("Files")
                .field("files", files)
                .field("total", total)
                .finish(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn hint(text: &str) -> TextHint {
        TextHint::of(text, false)
    }

    #[test]
    fn single_urls_are_links() {
        assert_eq!(hint("https://example.com/a?b=c"), TextHint::Link);
        assert_eq!(hint("  HTTP://example.com\n"), TextHint::Link);
        assert_eq!(hint("www.example.com"), TextHint::Link);
        assert_eq!(hint("mailto:me@example.com"), TextHint::Link);
        assert_eq!(hint("https://fa.wikipedia.org/wiki/تهران"), TextHint::Link);
    }

    #[test]
    fn text_around_a_url_is_not_a_link() {
        assert_eq!(hint("see https://example.com"), TextHint::Plain);
        assert_eq!(hint("https://"), TextHint::Plain);
        assert_eq!(hint("example.com"), TextHint::Plain);
        assert_eq!(hint("https://a.com https://b.com"), TextHint::Plain);
    }

    #[test]
    fn a_cut_text_is_never_a_link() {
        assert_eq!(
            TextHint::of("https://example.com/very", true),
            TextHint::Plain
        );
    }

    #[test]
    fn code_is_recognised() {
        let rust = "fn main() {\n    println!(\"hi\");\n}";
        assert_eq!(hint(rust), TextHint::Code);
        let python = "def add(a, b):\n    return a + b";
        assert_eq!(hint(python), TextHint::Code);
        let json = "{\n  \"a\": 1\n}";
        assert_eq!(hint(json), TextHint::Code);
    }

    #[test]
    fn prose_and_lists_are_plain() {
        assert_eq!(hint("Hello there"), TextHint::Plain);
        assert_eq!(hint("one line;"), TextHint::Plain);
        assert_eq!(hint("Shopping:\n  - milk\n  - bread"), TextHint::Plain);
        assert_eq!(hint("سلام\nخداحافظ"), TextHint::Plain);
        assert_eq!(
            hint("First paragraph.\n\nSecond paragraph."),
            TextHint::Plain
        );
    }

    #[test]
    fn debug_hides_text_and_paths() {
        let text = PreviewContent::Text {
            snippet: "hunter2".into(),
            truncated: false,
            hint: TextHint::Plain,
        };
        let files = PreviewContent::Files {
            files: vec![FileRef {
                path: "/Users/me/secret.txt".into(),
                size_bytes: None,
            }],
            total: 1,
        };
        let out = format!("{text:?} {files:?}");
        assert!(!out.contains("hunter2"), "leaked: {out}");
        assert!(!out.contains("secret"), "leaked: {out}");
    }
}
