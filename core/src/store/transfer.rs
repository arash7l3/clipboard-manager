//! Export and import of the history and snippets.
//!
//! The export is one binary blob so the apps only move bytes to and from a
//! file the user picks. It is **not encrypted**: it holds clipboard content in
//! the clear, so the apps must say so before writing one.
//!
//! Layout, all integers little-endian:
//!
//! ```text
//! "CLIPOEXP"  u32 version (1)
//! u64 item count, then per item (oldest first):
//!   u8 pinned, i64 created_at, i64 last_copied_at,
//!   option<source app: str id, option<str> name>,
//!   u8 kind (0 text, 1 image, 2 files), then
//!     text:  str plain, option<u8 format (0 rtf, 1 html), str data>
//!     image: u8 format (0 png, 1 tiff, 2 jpeg, 3 bmp), u32 width, u32 height,
//!            bytes data, option<bytes> thumbnail
//!     files: u64 count, then per file: str path, option<u64 size>
//! u64 snippet count, then per snippet: str title, str body, i64 created_at, i64 updated_at
//! ```
//!
//! `str` and `bytes` are a `u64` length and the data; `option` is a `u8` that
//! is 0 or 1, followed by the value when 1. Importing parses and checks the
//! whole blob before it changes anything.

use rusqlite::{params, Connection};

use super::snippets::insert_snippet;
use super::{insert_row, NewItem, Store};
use crate::error::CoreError;
use crate::model::{
    validate_snippet, FileRef, ImageContent, ImageFormat, ItemContent, RichText, RichTextFormat,
    SourceApp, TextContent, Timestamp,
};

const MAGIC: &[u8; 8] = b"CLIPOEXP";
const VERSION: u32 = 1;

/// What [`Store::import`] did.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct ImportSummary {
    /// History items added.
    pub items_added: usize,
    /// History items skipped because identical content was already stored.
    pub items_duplicate: usize,
    /// History items skipped for being larger than the current item size limit.
    pub items_too_large: usize,
    /// Snippets added.
    pub snippets_added: usize,
    /// Snippets skipped because one with the same title and body exists.
    pub snippets_duplicate: usize,
}

struct ExportItem {
    item: NewItem,
}

struct ExportSnippet {
    title: String,
    body: String,
    created_at: Timestamp,
    updated_at: Timestamp,
}

impl Store {
    /// Serialises the whole history and all snippets. See the module docs for
    /// the format. The result is unencrypted.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure or a corrupt row.
    pub fn export(&self) -> Result<Vec<u8>, CoreError> {
        let mut out = Writer(Vec::new());
        out.0.extend_from_slice(MAGIC);
        out.u32(VERSION);

        let items = self.list()?;
        out.len(items.len());
        // Oldest first, so importing in file order keeps the history order.
        for item in items.iter().rev() {
            out.u8(u8::from(item.pinned));
            out.i64(item.created_at.as_millis());
            out.i64(item.last_copied_at.as_millis());
            out.option(item.source_app.as_ref(), |out, app| {
                out.str(&app.id);
                out.option(app.name.as_deref(), Writer::str);
            });
            match &item.content {
                ItemContent::Text(text) => {
                    out.u8(0);
                    out.str(&text.plain);
                    out.option(text.rich.as_ref(), |out, rich| {
                        out.u8(rich_format_code(rich.format));
                        out.str(&rich.data);
                    });
                }
                ItemContent::Image(image) => {
                    out.u8(1);
                    out.u8(image_format_code(image.format));
                    out.u32(image.width);
                    out.u32(image.height);
                    out.bytes(&image.data);
                    out.option(image.thumbnail.as_deref(), Writer::bytes);
                }
                ItemContent::Files(files) => {
                    out.u8(2);
                    out.len(files.len());
                    for file in files {
                        out.str(&file.path);
                        out.option(file.size_bytes.as_ref(), |out, size| out.u64(*size));
                    }
                }
            }
        }

        let snippets = self.snippets("")?;
        out.len(snippets.len());
        for snippet in &snippets {
            out.str(&snippet.title);
            out.str(&snippet.body);
            out.i64(snippet.created_at.as_millis());
            out.i64(snippet.updated_at.as_millis());
        }
        Ok(out.0)
    }

    /// Adds the items and snippets of an export made by [`Store::export`].
    ///
    /// Content already stored is skipped, and items larger than the item size
    /// limit are skipped. Items keep their pin, source app and times, and
    /// rank as the most recent copies in their original order, so importing
    /// an old export can push existing unpinned items out past the history
    /// limit. Capture rules (private mode, ignored apps) do not apply: the
    /// user asked for this. Either everything is imported or, on error,
    /// nothing is.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidExport`] if `data` is not an export or is damaged,
    /// [`CoreError::UnsupportedExport`] if it comes from a newer Clipo, and
    /// [`CoreError::Storage`] on a database failure.
    pub fn import(&mut self, data: &[u8]) -> Result<ImportSummary, CoreError> {
        let (items, snippets) = parse(data)?;
        let mut summary = ImportSummary::default();

        let tx = self.conn.transaction()?;
        for ExportItem { item } in items {
            if item.content.byte_size() > self.limits.max_item_bytes {
                summary.items_too_large += 1;
                continue;
            }
            match insert_row(&tx, item) {
                Ok(_) => summary.items_added += 1,
                Err(CoreError::DuplicateContent) => summary.items_duplicate += 1,
                Err(err) => return Err(err),
            }
        }
        for snippet in snippets {
            if snippet_exists(&tx, &snippet)? {
                summary.snippets_duplicate += 1;
            } else {
                insert_snippet(
                    &tx,
                    &snippet.title,
                    &snippet.body,
                    snippet.created_at,
                    snippet.updated_at,
                )?;
                summary.snippets_added += 1;
            }
        }
        tx.commit()?;

        if summary.items_added > 0 {
            self.enforce_item_limit()?;
        }
        Ok(summary)
    }
}

fn snippet_exists(conn: &Connection, snippet: &ExportSnippet) -> Result<bool, CoreError> {
    Ok(conn.query_row(
        "SELECT EXISTS (SELECT 1 FROM snippets WHERE title = ?1 AND body = ?2)",
        params![snippet.title, snippet.body],
        |row| row.get(0),
    )?)
}

const fn rich_format_code(format: RichTextFormat) -> u8 {
    match format {
        RichTextFormat::Rtf => 0,
        RichTextFormat::Html => 1,
    }
}

const fn image_format_code(format: ImageFormat) -> u8 {
    match format {
        ImageFormat::Png => 0,
        ImageFormat::Tiff => 1,
        ImageFormat::Jpeg => 2,
        ImageFormat::Bmp => 3,
    }
}

struct Writer(Vec<u8>);

impl Writer {
    fn u8(&mut self, value: u8) {
        self.0.push(value);
    }

    fn u32(&mut self, value: u32) {
        self.0.extend_from_slice(&value.to_le_bytes());
    }

    fn u64(&mut self, value: u64) {
        self.0.extend_from_slice(&value.to_le_bytes());
    }

    fn i64(&mut self, value: i64) {
        self.0.extend_from_slice(&value.to_le_bytes());
    }

    fn len(&mut self, len: usize) {
        self.u64(u64::try_from(len).unwrap_or(u64::MAX));
    }

    fn bytes(&mut self, bytes: &[u8]) {
        self.len(bytes.len());
        self.0.extend_from_slice(bytes);
    }

    fn str(&mut self, text: &str) {
        self.bytes(text.as_bytes());
    }

    fn option<T>(&mut self, value: Option<T>, write: impl FnOnce(&mut Self, T)) {
        match value {
            Some(value) => {
                self.u8(1);
                write(self, value);
            }
            None => self.u8(0),
        }
    }
}

fn invalid(what: &'static str) -> CoreError {
    CoreError::InvalidExport(what)
}

/// Bounds-checked reader. Every length is checked against what is left before
/// anything is allocated, so a damaged length cannot cause a huge allocation.
struct Reader<'a>(&'a [u8]);

impl<'a> Reader<'a> {
    fn take(&mut self, n: usize) -> Result<&'a [u8], CoreError> {
        if n > self.0.len() {
            return Err(invalid("the data ends too early"));
        }
        let (head, rest) = self.0.split_at(n);
        self.0 = rest;
        Ok(head)
    }

    fn array<const N: usize>(&mut self) -> Result<[u8; N], CoreError> {
        let mut out = [0; N];
        out.copy_from_slice(self.take(N)?);
        Ok(out)
    }

    fn u8(&mut self) -> Result<u8, CoreError> {
        Ok(self.array::<1>()?[0])
    }

    fn u32(&mut self) -> Result<u32, CoreError> {
        Ok(u32::from_le_bytes(self.array()?))
    }

    fn u64(&mut self) -> Result<u64, CoreError> {
        Ok(u64::from_le_bytes(self.array()?))
    }

    fn i64(&mut self) -> Result<i64, CoreError> {
        Ok(i64::from_le_bytes(self.array()?))
    }

    fn count(&mut self) -> Result<usize, CoreError> {
        usize::try_from(self.u64()?).map_err(|_| invalid("a count is out of range"))
    }

    fn bytes(&mut self) -> Result<Vec<u8>, CoreError> {
        let len = self.count()?;
        Ok(self.take(len)?.to_vec())
    }

    fn str(&mut self) -> Result<String, CoreError> {
        let len = self.count()?;
        String::from_utf8(self.take(len)?.to_vec()).map_err(|_| invalid("text is not valid UTF-8"))
    }

    fn timestamp(&mut self) -> Result<Timestamp, CoreError> {
        Ok(Timestamp::from_millis(self.i64()?))
    }

    fn flag(&mut self) -> Result<bool, CoreError> {
        match self.u8()? {
            0 => Ok(false),
            1 => Ok(true),
            _ => Err(invalid("a flag is neither 0 nor 1")),
        }
    }

    fn option<T>(
        &mut self,
        read: impl FnOnce(&mut Self) -> Result<T, CoreError>,
    ) -> Result<Option<T>, CoreError> {
        if self.flag()? {
            read(self).map(Some)
        } else {
            Ok(None)
        }
    }
}

fn parse(data: &[u8]) -> Result<(Vec<ExportItem>, Vec<ExportSnippet>), CoreError> {
    let mut input = Reader(data);
    if input.take(MAGIC.len()).ok() != Some(MAGIC.as_slice()) {
        return Err(invalid("missing the Clipo export header"));
    }
    let version = input.u32()?;
    if version > VERSION {
        return Err(CoreError::UnsupportedExport {
            found: version,
            supported: VERSION,
        });
    }
    if version != VERSION {
        return Err(invalid("unknown format version"));
    }

    // No `with_capacity` from a count in the data: each entry takes at least
    // one byte, so the vectors can only grow as far as the data goes.
    let mut items = Vec::new();
    for _ in 0..input.count()? {
        items.push(read_item(&mut input)?);
    }
    let mut snippets = Vec::new();
    for _ in 0..input.count()? {
        let snippet = ExportSnippet {
            title: input.str()?,
            body: input.str()?,
            created_at: input.timestamp()?,
            updated_at: input.timestamp()?,
        };
        validate_snippet(&snippet.title, &snippet.body)?;
        snippets.push(snippet);
    }
    if !input.0.is_empty() {
        return Err(invalid("unexpected data after the end"));
    }
    Ok((items, snippets))
}

fn read_item(input: &mut Reader<'_>) -> Result<ExportItem, CoreError> {
    let pinned = input.flag()?;
    let created_at = input.timestamp()?;
    let last_copied_at = input.timestamp()?;
    let source_app = input.option(|input| {
        Ok(SourceApp {
            id: input.str()?,
            name: input.option(Reader::str)?,
        })
    })?;
    let content = match input.u8()? {
        0 => ItemContent::Text(TextContent {
            plain: input.str()?,
            rich: input.option(|input| {
                let format = match input.u8()? {
                    0 => RichTextFormat::Rtf,
                    1 => RichTextFormat::Html,
                    _ => return Err(invalid("unknown rich text format")),
                };
                Ok(RichText {
                    format,
                    data: input.str()?,
                })
            })?,
        }),
        1 => {
            let format = match input.u8()? {
                0 => ImageFormat::Png,
                1 => ImageFormat::Tiff,
                2 => ImageFormat::Jpeg,
                3 => ImageFormat::Bmp,
                _ => return Err(invalid("unknown image format")),
            };
            ItemContent::Image(ImageContent {
                format,
                width: input.u32()?,
                height: input.u32()?,
                data: input.bytes()?,
                thumbnail: input.option(Reader::bytes)?,
            })
        }
        2 => {
            let mut files = Vec::new();
            for _ in 0..input.count()? {
                files.push(FileRef {
                    path: input.str()?,
                    size_bytes: input.option(Reader::u64)?,
                });
            }
            ItemContent::Files(files)
        }
        _ => return Err(invalid("unknown item kind")),
    };
    content.validate()?;
    Ok(ExportItem {
        item: NewItem {
            content,
            source_app,
            pinned,
            created_at,
            last_copied_at,
        },
    })
}
