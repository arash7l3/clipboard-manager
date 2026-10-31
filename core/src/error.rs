//! Error types. Messages describe what went wrong without quoting clipboard content.

use thiserror::Error;

use crate::key::KeyStoreError;
use crate::model::{ItemId, SnippetId};

/// Any error returned by the core.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
pub enum CoreError {
    /// A captured item failed validation and was not stored.
    #[error("invalid clipboard content: {0}")]
    InvalidContent(#[from] InvalidContent),
    /// No item with this id exists.
    #[error("item {} not found", .0.as_raw())]
    ItemNotFound(ItemId),
    /// No snippet with this id exists.
    #[error("snippet {} not found", .0.as_raw())]
    SnippetNotFound(SnippetId),
    /// A snippet failed validation and was not stored.
    #[error("invalid snippet: {0}")]
    InvalidSnippet(#[from] InvalidSnippet),
    /// The data given to import is not a Clipo export. The message names the
    /// structural problem, never the content.
    #[error("not a valid Clipo export: {0}")]
    InvalidExport(&'static str),
    /// The export was written by a newer version of Clipo.
    #[error("export format version {found} is newer than supported version {supported}")]
    UnsupportedExport {
        /// Version found in the data.
        found: u32,
        /// Newest version this build understands.
        supported: u32,
    },
    /// An item with identical content is already stored.
    #[error("an item with the same content already exists")]
    DuplicateContent,
    /// A history limit is zero.
    #[error("history limits must be at least 1")]
    InvalidLimits,
    /// The auto-clear time is zero.
    #[error("auto-clear time must be longer than zero")]
    InvalidRules,
    /// Key bytes from the key store are not a valid database key.
    #[error("database key has the wrong length")]
    InvalidKey,
    /// The key does not open the database, or the file is not a Clipo database.
    #[error("the key does not open this database, or the file is not a Clipo database")]
    WrongKey,
    /// A database file exists but the key store holds no key for it. The data
    /// cannot be read; the app may offer to delete it and start over.
    #[error("the key store has no key for the existing database")]
    KeyMissing,
    /// The platform key store (Keychain, Credential Manager) failed.
    #[error("key store error: {0}")]
    KeyStore(#[from] KeyStoreError),
    /// The OS random number generator could not produce a key.
    #[error("system random number generator unavailable")]
    RandomUnavailable,
    /// SQLite was built without SQLCipher. The core refuses to store anything unencrypted.
    #[error("database encryption is unavailable")]
    EncryptionUnavailable,
    /// The database was written by a newer version of Clipo.
    #[error("database schema version {found} is newer than supported version {supported}")]
    UnsupportedSchema {
        /// Version found in the file.
        found: i64,
        /// Newest version this build understands.
        supported: i64,
    },
    /// File system error around the database file.
    #[error("I/O error: {0}")]
    Io(String),
    /// SQLite error, or a stored row that breaks the schema's invariants.
    #[error("storage error: {0}")]
    Storage(String),
}

// SQLite and I/O messages name tables, columns and error kinds, never bound
// values, so converting them to strings does not leak clipboard content.

impl From<rusqlite::Error> for CoreError {
    fn from(err: rusqlite::Error) -> Self {
        Self::Storage(err.to_string())
    }
}

impl From<std::io::Error> for CoreError {
    fn from(err: std::io::Error) -> Self {
        Self::Io(err.to_string())
    }
}

/// Why an [`ItemContent`](crate::ItemContent) was rejected.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Error)]
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

/// Why a snippet was rejected.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Error)]
pub enum InvalidSnippet {
    /// The title is empty or only whitespace.
    #[error("title is empty")]
    EmptyTitle,
    /// The body is empty.
    #[error("body is empty")]
    EmptyBody,
}
