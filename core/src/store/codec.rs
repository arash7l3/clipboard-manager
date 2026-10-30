//! Mapping between model types and table columns.
//!
//! The integer codes are part of the on-disk format: never renumber them.

use rusqlite::Row;

use crate::error::CoreError;
use crate::model::{
    ClipItem, FileRef, ImageContent, ImageFormat, ItemContent, ItemId, ItemKind, RichText,
    RichTextFormat, SourceApp, TextContent, Timestamp,
};

const KIND_TEXT: i64 = 0;
const KIND_IMAGE: i64 = 1;
const KIND_FILES: i64 = 2;

pub(super) const fn kind_code(kind: ItemKind) -> i64 {
    match kind {
        ItemKind::Text => KIND_TEXT,
        ItemKind::Image => KIND_IMAGE,
        ItemKind::Files => KIND_FILES,
    }
}

const fn rich_format_code(format: RichTextFormat) -> i64 {
    match format {
        RichTextFormat::Rtf => 0,
        RichTextFormat::Html => 1,
    }
}

fn rich_format_from_code(code: i64) -> Result<RichTextFormat, CoreError> {
    match code {
        0 => Ok(RichTextFormat::Rtf),
        1 => Ok(RichTextFormat::Html),
        _ => Err(corrupt("unknown rich text format")),
    }
}

const fn image_format_code(format: ImageFormat) -> i64 {
    match format {
        ImageFormat::Png => 0,
        ImageFormat::Tiff => 1,
        ImageFormat::Jpeg => 2,
        ImageFormat::Bmp => 3,
    }
}

fn image_format_from_code(code: i64) -> Result<ImageFormat, CoreError> {
    match code {
        0 => Ok(ImageFormat::Png),
        1 => Ok(ImageFormat::Tiff),
        2 => Ok(ImageFormat::Jpeg),
        3 => Ok(ImageFormat::Bmp),
        _ => Err(corrupt("unknown image format")),
    }
}

/// A row that breaks the schema's invariants. Names the problem, never the data.
pub(super) fn corrupt(what: &str) -> CoreError {
    CoreError::Storage(format!("corrupt item row: {what}"))
}

pub(super) fn to_i64(value: impl TryInto<i64>, what: &str) -> Result<i64, CoreError> {
    value
        .try_into()
        .map_err(|_| CoreError::Storage(format!("{what} out of range")))
}

/// Column values for one `items` row, borrowed from the content being stored.
/// Files go to `item_files` separately.
pub(super) struct ItemColumns<'a> {
    pub(super) kind: i64,
    pub(super) byte_size: i64,
    pub(super) text_plain: Option<&'a str>,
    pub(super) rich_format: Option<i64>,
    pub(super) rich_data: Option<&'a str>,
    pub(super) image_format: Option<i64>,
    pub(super) image_width: Option<i64>,
    pub(super) image_height: Option<i64>,
    pub(super) image_data: Option<&'a [u8]>,
    pub(super) thumbnail: Option<&'a [u8]>,
}

impl<'a> ItemColumns<'a> {
    pub(super) fn new(content: &'a ItemContent) -> Result<Self, CoreError> {
        let mut columns = Self {
            kind: kind_code(content.kind()),
            byte_size: to_i64(content.byte_size(), "item size")?,
            text_plain: None,
            rich_format: None,
            rich_data: None,
            image_format: None,
            image_width: None,
            image_height: None,
            image_data: None,
            thumbnail: None,
        };
        match content {
            ItemContent::Text(text) => {
                columns.text_plain = Some(&text.plain);
                if let Some(rich) = &text.rich {
                    columns.rich_format = Some(rich_format_code(rich.format));
                    columns.rich_data = Some(&rich.data);
                }
            }
            ItemContent::Image(image) => {
                columns.image_format = Some(image_format_code(image.format));
                columns.image_width = Some(i64::from(image.width));
                columns.image_height = Some(i64::from(image.height));
                columns.image_data = Some(&image.data);
                columns.thumbnail = image.thumbnail.as_deref();
            }
            ItemContent::Files(_) => {}
        }
        Ok(columns)
    }
}

/// Columns selected for an item, in the order [`RawItem::from_row`] reads them.
macro_rules! item_columns {
    () => {
        "id, kind, pinned, source_app_id, source_app_name, created_at, last_copied_at, \
         text_plain, rich_format, rich_data, image_format, image_width, image_height, \
         image_data, thumbnail"
    };
}
pub(super) use item_columns;

/// One `items` row as stored, before it is checked and turned into a [`ClipItem`].
pub(super) struct RawItem {
    id: i64,
    kind: i64,
    pinned: bool,
    source_app_id: Option<String>,
    source_app_name: Option<String>,
    created_at: i64,
    last_copied_at: i64,
    text_plain: Option<String>,
    rich_format: Option<i64>,
    rich_data: Option<String>,
    image_format: Option<i64>,
    image_width: Option<i64>,
    image_height: Option<i64>,
    image_data: Option<Vec<u8>>,
    thumbnail: Option<Vec<u8>>,
}

impl RawItem {
    pub(super) fn from_row(row: &Row<'_>) -> rusqlite::Result<Self> {
        Ok(Self {
            id: row.get(0)?,
            kind: row.get(1)?,
            pinned: row.get(2)?,
            source_app_id: row.get(3)?,
            source_app_name: row.get(4)?,
            created_at: row.get(5)?,
            last_copied_at: row.get(6)?,
            text_plain: row.get(7)?,
            rich_format: row.get(8)?,
            rich_data: row.get(9)?,
            image_format: row.get(10)?,
            image_width: row.get(11)?,
            image_height: row.get(12)?,
            image_data: row.get(13)?,
            thumbnail: row.get(14)?,
        })
    }

    pub(super) const fn id(&self) -> i64 {
        self.id
    }

    pub(super) const fn is_files(&self) -> bool {
        self.kind == KIND_FILES
    }

    /// Builds the item. `files` is the item's file list, loaded by the caller
    /// for file items and empty otherwise.
    pub(super) fn into_item(self, files: Vec<FileRef>) -> Result<ClipItem, CoreError> {
        let content = match self.kind {
            KIND_TEXT => ItemContent::Text(TextContent {
                plain: self.text_plain.ok_or_else(|| corrupt("text missing"))?,
                rich: match (self.rich_format, self.rich_data) {
                    (Some(format), Some(data)) => Some(RichText {
                        format: rich_format_from_code(format)?,
                        data,
                    }),
                    (None, None) => None,
                    _ => return Err(corrupt("rich text incomplete")),
                },
            }),
            KIND_IMAGE => ItemContent::Image(ImageContent {
                format: image_format_from_code(
                    self.image_format
                        .ok_or_else(|| corrupt("image format missing"))?,
                )?,
                width: dimension(self.image_width)?,
                height: dimension(self.image_height)?,
                data: self
                    .image_data
                    .ok_or_else(|| corrupt("image data missing"))?,
                thumbnail: self.thumbnail,
            }),
            KIND_FILES if files.is_empty() => return Err(corrupt("file item without files")),
            KIND_FILES => ItemContent::Files(files),
            _ => return Err(corrupt("unknown kind")),
        };
        Ok(ClipItem {
            id: ItemId::from_raw(self.id),
            content,
            pinned: self.pinned,
            source_app: self.source_app_id.map(|id| SourceApp {
                id,
                name: self.source_app_name,
            }),
            created_at: Timestamp::from_millis(self.created_at),
            last_copied_at: Timestamp::from_millis(self.last_copied_at),
        })
    }
}

fn dimension(value: Option<i64>) -> Result<u32, CoreError> {
    value
        .and_then(|v| u32::try_from(v).ok())
        .filter(|&v| v > 0)
        .ok_or_else(|| corrupt("bad image dimension"))
}

/// Reads one `item_files` row: `path, size_bytes`.
pub(super) fn file_from_row(row: &Row<'_>) -> rusqlite::Result<(String, Option<i64>)> {
    Ok((row.get(0)?, row.get(1)?))
}

pub(super) fn file_ref(path: String, size: Option<i64>) -> Result<FileRef, CoreError> {
    let size_bytes = size
        .map(|s| u64::try_from(s).map_err(|_| corrupt("negative file size")))
        .transpose()?;
    Ok(FileRef { path, size_bytes })
}
