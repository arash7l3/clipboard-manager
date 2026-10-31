//! The payload of a history item: text, an image or a list of files.

use std::borrow::Cow;
use std::ffi::OsStr;
use std::fmt;
use std::path::Path;

use sha2::{Digest, Sha256};

use super::redact::Redacted;
use crate::error::InvalidContent;

/// The broad kind of an item. Each kind has its own tab in the popup.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ItemKind {
    /// Plain or rich text, including links and code.
    Text,
    /// A bitmap image.
    Image,
    /// One or more copied file or folder paths.
    Files,
}

/// What was copied. Exactly one representation per item.
#[derive(Clone, PartialEq, Eq)]
pub enum ItemContent {
    /// Text, with an optional rich representation.
    Text(TextContent),
    /// An encoded image.
    Image(ImageContent),
    /// Copied files, in the order the source app listed them.
    Files(Vec<FileRef>),
}

/// Text content. `plain` is always present; `rich` keeps formatting for restore.
#[derive(Clone, PartialEq, Eq)]
pub struct TextContent {
    /// Plain-text representation, used for display, search and dedup.
    pub plain: String,
    /// Rich representation as the source app provided it, if any.
    pub rich: Option<RichText>,
}

/// Formatted text in a platform interchange format.
#[derive(Clone, PartialEq, Eq)]
pub struct RichText {
    /// Format of `data`.
    pub format: RichTextFormat,
    /// The formatted text, for example an RTF document or an HTML fragment.
    pub data: String,
}

/// Rich-text interchange formats understood by both platforms.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum RichTextFormat {
    /// Rich Text Format.
    Rtf,
    /// HTML fragment.
    Html,
}

/// An encoded image plus an optional pre-rendered thumbnail.
#[derive(Clone, PartialEq, Eq)]
pub struct ImageContent {
    /// Encoding of `data`.
    pub format: ImageFormat,
    /// Width in pixels.
    pub width: u32,
    /// Height in pixels.
    pub height: u32,
    /// The encoded image as captured.
    pub data: Vec<u8>,
    /// Small PNG for the popup, rendered by the platform app. Not part of dedup.
    pub thumbnail: Option<Vec<u8>>,
}

/// Image encodings the platform apps may hand over.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ImageFormat {
    /// PNG.
    Png,
    /// TIFF (common on the macOS pasteboard).
    Tiff,
    /// JPEG.
    Jpeg,
    /// Windows bitmap.
    Bmp,
}

impl ImageFormat {
    /// Stable tag used in [`ContentHash`]. Never renumber: stored hashes depend on it.
    const fn hash_tag(self) -> u8 {
        match self {
            Self::Png => 0,
            Self::Tiff => 1,
            Self::Jpeg => 2,
            Self::Bmp => 3,
        }
    }
}

/// One copied file or folder.
#[derive(Clone, PartialEq, Eq)]
pub struct FileRef {
    /// Absolute path as reported by the OS.
    pub path: String,
    /// Size in bytes at capture time; `None` for folders or when unknown.
    pub size_bytes: Option<u64>,
}

impl FileRef {
    /// The last path component, for display. `None` for a root path.
    #[must_use]
    pub fn name(&self) -> Option<&str> {
        Path::new(&self.path).file_name().and_then(OsStr::to_str)
    }
}

/// SHA-256 over the parts of an item's content that define its identity.
///
/// Two captures with equal hashes are the same item for dedup purposes.
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
pub struct ContentHash([u8; 32]);

impl ContentHash {
    /// Wraps raw hash bytes, for example when loading from storage.
    #[must_use]
    pub const fn from_bytes(bytes: [u8; 32]) -> Self {
        Self(bytes)
    }

    /// The raw hash bytes.
    #[must_use]
    pub const fn as_bytes(&self) -> &[u8; 32] {
        &self.0
    }
}

/// Domain-separation prefix for [`ContentHash`]. Bump the version if the
/// encoding below ever changes, and migrate stored hashes.
const HASH_DOMAIN: &[u8] = b"clipo-content-v1";

impl ItemContent {
    /// The kind (tab) this content belongs to.
    #[must_use]
    pub const fn kind(&self) -> ItemKind {
        match self {
            Self::Text(_) => ItemKind::Text,
            Self::Image(_) => ItemKind::Image,
            Self::Files(_) => ItemKind::Files,
        }
    }

    /// Approximate payload size in bytes, used for size limits.
    #[must_use]
    pub fn byte_size(&self) -> usize {
        match self {
            Self::Text(t) => t.plain.len() + t.rich.as_ref().map_or(0, |r| r.data.len()),
            Self::Image(i) => i.data.len() + i.thumbnail.as_ref().map_or(0, Vec::len),
            Self::Files(files) => files.iter().map(|f| f.path.len()).sum(),
        }
    }

    /// The content as plain text, for "paste as plain text". Text gives its
    /// plain form without formatting; files give their paths, one per line;
    /// an image has no text form.
    #[must_use]
    pub fn plain_text(&self) -> Option<Cow<'_, str>> {
        match self {
            Self::Text(t) => Some(Cow::Borrowed(&t.plain)),
            Self::Image(_) => None,
            Self::Files(files) => Some(Cow::Owned(
                files
                    .iter()
                    .map(|f| f.path.as_str())
                    .collect::<Vec<_>>()
                    .join("\n"),
            )),
        }
    }

    /// Checks the structural rules every stored item must meet.
    ///
    /// # Errors
    ///
    /// Returns the first rule the content breaks.
    pub fn validate(&self) -> Result<(), InvalidContent> {
        match self {
            Self::Text(t) if t.plain.is_empty() => Err(InvalidContent::EmptyText),
            Self::Image(i) if i.data.is_empty() => Err(InvalidContent::EmptyImage),
            Self::Image(i) if i.width == 0 || i.height == 0 => {
                Err(InvalidContent::ZeroImageDimension)
            }
            Self::Files(files) if files.is_empty() => Err(InvalidContent::NoFiles),
            Self::Files(files) if files.iter().any(|f| f.path.is_empty()) => {
                Err(InvalidContent::EmptyFilePath)
            }
            _ => Ok(()),
        }
    }

    /// Identity hash for dedup.
    ///
    /// Covers the plain text (not its rich form), the image format and bytes
    /// (not size or thumbnail), and the ordered file paths (not sizes). Fields
    /// are length-prefixed so different splits of the same bytes never collide.
    #[must_use]
    pub fn content_hash(&self) -> ContentHash {
        let mut hasher = Sha256::new();
        hasher.update(HASH_DOMAIN);
        match self {
            Self::Text(t) => {
                hasher.update([0]);
                update_field(&mut hasher, t.plain.as_bytes());
            }
            Self::Image(i) => {
                hasher.update([1, i.format.hash_tag()]);
                update_field(&mut hasher, &i.data);
            }
            Self::Files(files) => {
                hasher.update([2]);
                hasher.update(len_prefix(files.len()));
                for file in files {
                    update_field(&mut hasher, file.path.as_bytes());
                }
            }
        }
        ContentHash(hasher.finalize().into())
    }
}

fn update_field(hasher: &mut Sha256, bytes: &[u8]) {
    hasher.update(len_prefix(bytes.len()));
    hasher.update(bytes);
}

fn len_prefix(len: usize) -> [u8; 8] {
    u64::try_from(len).unwrap_or(u64::MAX).to_le_bytes()
}

// Hand-written Debug impls: never print clipboard text, bytes or paths.

impl fmt::Debug for ItemContent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Text(t) => f.debug_tuple("Text").field(t).finish(),
            Self::Image(i) => f.debug_tuple("Image").field(i).finish(),
            Self::Files(files) => f.debug_tuple("Files").field(files).finish(),
        }
    }
}

impl fmt::Debug for TextContent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("TextContent")
            .field("plain", &Redacted(self.plain.len()))
            .field("rich", &self.rich)
            .finish()
    }
}

impl fmt::Debug for RichText {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("RichText")
            .field("format", &self.format)
            .field("data", &Redacted(self.data.len()))
            .finish()
    }
}

impl fmt::Debug for ImageContent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("ImageContent")
            .field("format", &self.format)
            .field("width", &self.width)
            .field("height", &self.height)
            .field("data", &Redacted(self.data.len()))
            .field(
                "thumbnail",
                &self.thumbnail.as_ref().map(|t| Redacted(t.len())),
            )
            .finish()
    }
}

impl fmt::Debug for FileRef {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("FileRef")
            .field("path", &Redacted(self.path.len()))
            .field("size_bytes", &self.size_bytes)
            .finish()
    }
}

impl fmt::Debug for ContentHash {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        // A hash of a short secret can be brute-forced, so keep it out of logs too.
        f.write_str("ContentHash(<sha256>)")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn text(plain: &str) -> ItemContent {
        ItemContent::Text(TextContent {
            plain: plain.into(),
            rich: None,
        })
    }

    fn image(format: ImageFormat, data: &[u8]) -> ItemContent {
        ItemContent::Image(ImageContent {
            format,
            width: 2,
            height: 3,
            data: data.to_vec(),
            thumbnail: None,
        })
    }

    fn files(paths: &[&str]) -> ItemContent {
        ItemContent::Files(
            paths
                .iter()
                .map(|p| FileRef {
                    path: (*p).into(),
                    size_bytes: Some(10),
                })
                .collect(),
        )
    }

    fn hex(hash: ContentHash) -> String {
        use std::fmt::Write;
        hash.as_bytes().iter().fold(String::new(), |mut s, b| {
            let _ = write!(s, "{b:02x}");
            s
        })
    }

    #[test]
    fn kind_follows_variant() {
        assert_eq!(text("a").kind(), ItemKind::Text);
        assert_eq!(image(ImageFormat::Png, b"x").kind(), ItemKind::Image);
        assert_eq!(files(&["/a"]).kind(), ItemKind::Files);
    }

    #[test]
    fn validate_accepts_well_formed_content() {
        assert_eq!(text(" ").validate(), Ok(()));
        assert_eq!(image(ImageFormat::Png, b"x").validate(), Ok(()));
        assert_eq!(files(&["/a", "/b"]).validate(), Ok(()));
    }

    #[test]
    fn validate_rejects_malformed_content() {
        assert_eq!(text("").validate(), Err(InvalidContent::EmptyText));
        assert_eq!(
            image(ImageFormat::Png, b"").validate(),
            Err(InvalidContent::EmptyImage)
        );
        let ItemContent::Image(mut img) = image(ImageFormat::Png, b"x") else {
            unreachable!()
        };
        img.width = 0;
        assert_eq!(
            ItemContent::Image(img).validate(),
            Err(InvalidContent::ZeroImageDimension)
        );
        assert_eq!(files(&[]).validate(), Err(InvalidContent::NoFiles));
        assert_eq!(
            files(&["/a", ""]).validate(),
            Err(InvalidContent::EmptyFilePath)
        );
    }

    #[test]
    fn byte_size_counts_all_payloads() {
        let rich = ItemContent::Text(TextContent {
            plain: "abc".into(),
            rich: Some(RichText {
                format: RichTextFormat::Html,
                data: "<b>abc</b>".into(),
            }),
        });
        assert_eq!(rich.byte_size(), 3 + 10);

        let ItemContent::Image(mut img) = image(ImageFormat::Png, b"1234") else {
            unreachable!()
        };
        img.thumbnail = Some(vec![0; 6]);
        assert_eq!(ItemContent::Image(img).byte_size(), 10);

        assert_eq!(files(&["/ab", "/c"]).byte_size(), 5);
    }

    #[test]
    fn file_name_is_last_component() {
        let file = |path: &str| FileRef {
            path: path.into(),
            size_bytes: None,
        };
        assert_eq!(file("/Users/me/report.pdf").name(), Some("report.pdf"));
        assert_eq!(file("/Users/me/folder/").name(), Some("folder"));
        assert_eq!(file("/").name(), None);
    }

    #[test]
    fn hash_ignores_rich_form_of_text() {
        let rich = ItemContent::Text(TextContent {
            plain: "hello".into(),
            rich: Some(RichText {
                format: RichTextFormat::Rtf,
                data: "{\\rtf1 hello}".into(),
            }),
        });
        assert_eq!(rich.content_hash(), text("hello").content_hash());
        assert_ne!(text("hello").content_hash(), text("hello ").content_hash());
    }

    #[test]
    fn hash_ignores_image_size_and_thumbnail_but_not_format() {
        let ItemContent::Image(mut img) = image(ImageFormat::Png, b"pixels") else {
            unreachable!()
        };
        img.width = 99;
        img.thumbnail = Some(vec![1, 2, 3]);
        let base = image(ImageFormat::Png, b"pixels").content_hash();
        assert_eq!(ItemContent::Image(img).content_hash(), base);
        assert_ne!(image(ImageFormat::Tiff, b"pixels").content_hash(), base);
    }

    #[test]
    fn hash_covers_file_order_but_not_sizes() {
        let ItemContent::Files(mut list) = files(&["/a", "/b"]) else {
            unreachable!()
        };
        list[0].size_bytes = None;
        assert_eq!(
            ItemContent::Files(list).content_hash(),
            files(&["/a", "/b"]).content_hash()
        );
        assert_ne!(
            files(&["/a", "/b"]).content_hash(),
            files(&["/b", "/a"]).content_hash()
        );
    }

    #[test]
    fn hash_separates_kinds_and_field_boundaries() {
        assert_ne!(text("/a").content_hash(), files(&["/a"]).content_hash());
        assert_ne!(
            text("/a").content_hash(),
            image(ImageFormat::Png, b"/a").content_hash()
        );
        assert_ne!(
            files(&["/a/b", "/c"]).content_hash(),
            files(&["/a", "/b/c"]).content_hash()
        );
    }

    #[test]
    fn hash_encoding_is_stable() {
        // Stored hashes depend on this value. If it changes, bump HASH_DOMAIN
        // and migrate the store instead of updating the expected string.
        assert_eq!(
            hex(text("hello").content_hash()),
            "475e00c694d20d1cb7dc3ee6a309c5be911d6a6e7489e8ef9b5cce4c075e752b"
        );
    }

    #[test]
    fn debug_never_prints_content() {
        let secret = "hunter2-secret";
        let samples = [
            ItemContent::Text(TextContent {
                plain: secret.into(),
                rich: Some(RichText {
                    format: RichTextFormat::Html,
                    data: format!("<i>{secret}</i>"),
                }),
            }),
            ItemContent::Image(ImageContent {
                format: ImageFormat::Png,
                width: 1,
                height: 1,
                data: secret.as_bytes().to_vec(),
                thumbnail: Some(secret.as_bytes().to_vec()),
            }),
            ItemContent::Files(vec![FileRef {
                path: format!("/Users/{secret}/x"),
                size_bytes: Some(1),
            }]),
        ];
        for content in &samples {
            let out = format!("{content:?} {:?}", content.content_hash());
            assert!(!out.contains(secret), "leaked: {out}");
            assert!(!out.contains("104, 117, 110"), "leaked bytes: {out}");
        }
    }
}
