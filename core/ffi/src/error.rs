//! Errors as seen by Swift (`ClipoError`, `KeyStoreError`) and C#
//! (`ClipoException`, `KeyStoreException`). Like the core's, they never carry
//! clipboard content.

/// Any error returned by a [`ClipStore`](crate::ClipStore) call. Mirrors
/// [`clipo_core::CoreError`]; see there for when each one happens.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error, uniffi::Error)]
pub enum ClipoError {
    /// A captured item failed validation and was not stored.
    #[error("invalid clipboard content: {reason}")]
    InvalidContent {
        /// What was wrong with it.
        reason: InvalidContent,
    },
    /// No item with this id exists.
    #[error("item {id} not found")]
    ItemNotFound {
        /// The id that was asked for.
        id: i64,
    },
    /// No snippet with this id exists.
    #[error("snippet {id} not found")]
    SnippetNotFound {
        /// The id that was asked for.
        id: i64,
    },
    /// A snippet failed validation and was not stored.
    #[error("invalid snippet: {reason}")]
    InvalidSnippet {
        /// What was wrong with it.
        reason: InvalidSnippet,
    },
    /// The data to import is not a Clipo export, or is damaged.
    #[error("not a valid Clipo export: {message}")]
    InvalidExport {
        /// What is structurally wrong; never quotes content.
        message: String,
    },
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
    /// The key store returned bytes that are not a valid database key.
    #[error("database key has the wrong length")]
    InvalidKey,
    /// The key does not open the database, or the file is not a Clipo database.
    #[error("the key does not open this database, or the file is not a Clipo database")]
    WrongKey,
    /// A database file exists but the key store holds no key for it. Offer to
    /// start over with [`delete_database`](crate::delete_database).
    #[error("the key store has no key for the existing database")]
    KeyMissing,
    /// The platform key store threw [`KeyStoreError`].
    #[error("key store error: {message}")]
    KeyStore {
        /// The message the key store gave.
        message: String,
    },
    /// The OS random number generator could not produce a key.
    #[error("system random number generator unavailable")]
    RandomUnavailable,
    /// The library was built without SQLCipher. Nothing is stored unencrypted.
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
    #[error("I/O error: {message}")]
    Io {
        /// What went wrong.
        message: String,
    },
    /// SQLite error, or a stored row that breaks the schema's invariants.
    #[error("storage error: {message}")]
    Storage {
        /// What went wrong.
        message: String,
    },
}

impl From<clipo_core::CoreError> for ClipoError {
    fn from(err: clipo_core::CoreError) -> Self {
        use clipo_core::CoreError as E;
        match err {
            E::InvalidContent(reason) => Self::InvalidContent {
                reason: reason.into(),
            },
            E::ItemNotFound(id) => Self::ItemNotFound { id: id.as_raw() },
            E::SnippetNotFound(id) => Self::SnippetNotFound { id: id.as_raw() },
            E::InvalidSnippet(reason) => Self::InvalidSnippet {
                reason: reason.into(),
            },
            E::InvalidExport(message) => Self::InvalidExport {
                message: message.to_owned(),
            },
            E::UnsupportedExport { found, supported } => {
                Self::UnsupportedExport { found, supported }
            }
            E::DuplicateContent => Self::DuplicateContent,
            E::InvalidLimits => Self::InvalidLimits,
            E::InvalidRules => Self::InvalidRules,
            E::InvalidKey => Self::InvalidKey,
            E::WrongKey => Self::WrongKey,
            E::KeyMissing => Self::KeyMissing,
            E::KeyStore(err) => Self::KeyStore {
                message: err.message,
            },
            E::RandomUnavailable => Self::RandomUnavailable,
            E::EncryptionUnavailable => Self::EncryptionUnavailable,
            E::UnsupportedSchema { found, supported } => {
                Self::UnsupportedSchema { found, supported }
            }
            E::Io(message) => Self::Io { message },
            E::Storage(message) => Self::Storage { message },
        }
    }
}

/// Why captured content was rejected. Mirrors [`clipo_core::InvalidContent`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error, uniffi::Enum)]
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

impl From<clipo_core::InvalidContent> for InvalidContent {
    fn from(reason: clipo_core::InvalidContent) -> Self {
        use clipo_core::InvalidContent as R;
        match reason {
            R::EmptyText => Self::EmptyText,
            R::EmptyImage => Self::EmptyImage,
            R::ZeroImageDimension => Self::ZeroImageDimension,
            R::NoFiles => Self::NoFiles,
            R::EmptyFilePath => Self::EmptyFilePath,
        }
    }
}

/// Why a snippet was rejected. Mirrors [`clipo_core::InvalidSnippet`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, thiserror::Error, uniffi::Enum)]
pub enum InvalidSnippet {
    /// The title is empty or only whitespace.
    #[error("title is empty")]
    EmptyTitle,
    /// The body is empty.
    #[error("body is empty")]
    EmptyBody,
}

impl From<clipo_core::InvalidSnippet> for InvalidSnippet {
    fn from(reason: clipo_core::InvalidSnippet) -> Self {
        match reason {
            clipo_core::InvalidSnippet::EmptyTitle => Self::EmptyTitle,
            clipo_core::InvalidSnippet::EmptyBody => Self::EmptyBody,
        }
    }
}

/// Thrown by a platform [`KeyStore`](crate::KeyStore). The message must
/// describe the failure (for example an OS status code) and never contain key
/// material.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error, uniffi::Error)]
pub enum KeyStoreError {
    /// Reading or writing the credential store failed.
    #[error("{message}")]
    Failed {
        /// What went wrong, for diagnostics.
        message: String,
    },
}

impl From<KeyStoreError> for clipo_core::KeyStoreError {
    fn from(err: KeyStoreError) -> Self {
        let KeyStoreError::Failed { message } = err;
        Self::new(message)
    }
}

/// The foreign implementation threw something other than [`KeyStoreError`].
impl From<uniffi::UnexpectedUniFFICallbackError> for KeyStoreError {
    fn from(err: uniffi::UnexpectedUniFFICallbackError) -> Self {
        Self::Failed {
            message: format!("unexpected key store error: {}", err.reason),
        }
    }
}
