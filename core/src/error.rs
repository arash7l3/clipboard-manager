//! Error types. Messages describe what went wrong without quoting clipboard content.

use thiserror::Error;

/// Any error returned by the core.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
#[non_exhaustive]
pub enum CoreError {
    /// A captured item failed validation and was not stored.
    #[error("invalid clipboard content: {0}")]
    InvalidContent(#[from] InvalidContent),
}

/// Why an [`ItemContent`](crate::ItemContent) was rejected.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Error)]
#[non_exhaustive]
pub enum InvalidContent {
    /// Text item with an empty plain-text representation.
    #[error("text is empty")]
    EmptyText,
    /// Image item with no encoded bytes.
    #[error("image data is empty")]
    EmptyImage,
    /// Image item with a zero width or height.
    #[error("image has a zero dimension")]
    ZeroImageDimension,
    /// File item with no files.
    #[error("file list is empty")]
    NoFiles,
    /// File item containing an empty path.
    #[error("file path is empty")]
    EmptyFilePath,
}
