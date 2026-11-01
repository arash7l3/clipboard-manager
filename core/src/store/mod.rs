//! Encrypted on-disk history store.
//!
//! One SQLCipher database holds the whole history. Every page, the WAL and the
//! journal are encrypted with a raw 256-bit key from the OS credential store,
//! so content and metadata alike are unreadable without it. See
//! `docs/decisions/0007-storage-encryption.md`.

mod codec;
mod history;
mod rules;
mod schema;
mod search;
mod snippets;
mod transfer;

use std::borrow::Cow;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::time::Duration;

use rusqlite::{params, Connection, ErrorCode, OptionalExtension};

use crate::error::CoreError;
use crate::key::{load_key, DatabaseKey, KeyStore};
use crate::model::{ClipItem, ContentHash, FileRef, ItemContent, ItemId, SourceApp, Timestamp};
use codec::{item_columns, ItemColumns, RawItem};
pub use history::{HistoryLimits, Recorded, SkipReason, DEFAULT_MAX_ITEMS, DEFAULT_MAX_ITEM_BYTES};
pub use rules::CaptureRules;
pub use search::Query;
pub use transfer::ImportSummary;

/// How long a write waits for a lock held by another connection.
const BUSY_TIMEOUT: Duration = Duration::from_secs(5);

/// The clipboard history database.
///
/// Holds one SQLite connection: `Send` but not `Sync`. Wrap it in a mutex to
/// share it between threads.
pub struct Store {
    conn: Connection,
    limits: HistoryLimits,
    rules: CaptureRules,
}

impl Store {
    /// Opens or creates the database at `path` with the key held in `keys`.
    ///
    /// On first run (no key, no file) it generates a key, saves it to `keys`,
    /// then creates the database. It never replaces a key on its own: if a
    /// database file exists but `keys` is empty it fails with
    /// [`CoreError::KeyMissing`], leaving the choice to reset to the user (see
    /// [`Store::delete_database`]).
    ///
    /// # Errors
    ///
    /// [`CoreError::KeyMissing`], [`CoreError::KeyStore`], [`CoreError::InvalidKey`],
    /// [`CoreError::RandomUnavailable`], and anything [`Store::open`] returns.
    pub fn open_with_key_store(path: &Path, keys: &dyn KeyStore) -> Result<Self, CoreError> {
        if let Some(key) = load_key(keys)? {
            return Self::open(path, &key);
        }
        if path.try_exists()? {
            return Err(CoreError::KeyMissing);
        }
        let key = DatabaseKey::generate()?;
        // Save before creating the file, so a database never exists without its key.
        keys.save(key.as_bytes())?;
        Self::open(path, &key)
    }

    /// Opens or creates the database at `path`, encrypted with `key`.
    ///
    /// Creates missing parent directories. On Unix a new file is created
    /// readable by the owner only.
    ///
    /// # Errors
    ///
    /// [`CoreError::WrongKey`] if `key` does not open an existing file,
    /// [`CoreError::EncryptionUnavailable`] if SQLCipher is missing,
    /// [`CoreError::UnsupportedSchema`] for a file from a newer Clipo, and
    /// [`CoreError::Io`] or [`CoreError::Storage`] for other failures.
    pub fn open(path: &Path, key: &DatabaseKey) -> Result<Self, CoreError> {
        create_private_file(path)?;
        let mut conn = Connection::open(path)?;
        unlock(&conn, key)?;
        configure(&conn)?;
        search::register_functions(&conn)?;
        schema::migrate(&mut conn)?;
        Ok(Self {
            conn,
            limits: HistoryLimits::default(),
            rules: CaptureRules::default(),
        })
    }

    /// Deletes the database at `path` with its WAL, shared-memory and journal
    /// files. Missing files are ignored. Use it to reset after
    /// [`CoreError::KeyMissing`] or [`CoreError::WrongKey`]; all history is lost.
    ///
    /// # Errors
    ///
    /// [`CoreError::Io`] if a file exists but cannot be removed.
    pub fn delete_database(path: &Path) -> Result<(), CoreError> {
        for suffix in ["", "-wal", "-shm", "-journal"] {
            let mut file = path.as_os_str().to_owned();
            file.push(suffix);
            match fs::remove_file(PathBuf::from(file)) {
                Err(err) if err.kind() != io::ErrorKind::NotFound => return Err(err.into()),
                _ => {}
            }
        }
        Ok(())
    }

    /// Stores new content as an unpinned item copied at `at`, first in the history.
    ///
    /// Low-level: no duplicate merging and no limits. Watchers use
    /// [`Store::record`].
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidContent`] if the content fails validation,
    /// [`CoreError::DuplicateContent`] if identical content is already stored.
    pub fn insert(
        &mut self,
        content: ItemContent,
        source_app: Option<SourceApp>,
        at: Timestamp,
    ) -> Result<ClipItem, CoreError> {
        let tx = self.conn.transaction()?;
        let item = insert_row(
            &tx,
            NewItem {
                content,
                source_app,
                pinned: false,
                created_at: at,
                last_copied_at: at,
            },
        )?;
        tx.commit()?;
        Ok(item)
    }

    /// The item with this id, if it exists.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure or a corrupt row.
    pub fn get(&self, id: ItemId) -> Result<Option<ClipItem>, CoreError> {
        let raw = self
            .conn
            .prepare_cached(concat!(
                "SELECT ",
                item_columns!(),
                " FROM items WHERE id = ?1"
            ))?
            .query_row([id.as_raw()], RawItem::from_row)
            .optional()?;
        raw.map(|raw| self.finish(raw)).transpose()
    }

    /// All items, most recently copied first (see [`ClipItem::last_copied_at`]).
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure or a corrupt row.
    pub fn list(&self) -> Result<Vec<ClipItem>, CoreError> {
        let raws = self
            .conn
            .prepare_cached(concat!(
                "SELECT ",
                item_columns!(),
                " FROM items ORDER BY copy_order DESC"
            ))?
            .query_map([], RawItem::from_row)?
            .collect::<Result<Vec<_>, _>>()?;
        raws.into_iter().map(|raw| self.finish(raw)).collect()
    }

    /// The id of the item whose content has this hash, if any.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn find_by_hash(&self, hash: &ContentHash) -> Result<Option<ItemId>, CoreError> {
        Ok(self
            .conn
            .prepare_cached("SELECT id FROM items WHERE content_hash = ?1")?
            .query_row([hash.as_bytes().as_slice()], |row| row.get(0))
            .optional()?
            .map(ItemId::from_raw))
    }

    /// Records that the item was copied again at `at`, moving it to the top
    /// whatever `at` is.
    ///
    /// # Errors
    ///
    /// [`CoreError::ItemNotFound`] if no item has this id.
    pub fn touch(&mut self, id: ItemId, at: Timestamp) -> Result<(), CoreError> {
        let changed = self.conn.execute(
            "UPDATE items SET last_copied_at = ?2, \
             copy_order = (SELECT max(copy_order) + 1 FROM items) WHERE id = ?1",
            params![id.as_raw(), at.as_millis()],
        )?;
        expect_one(changed, id)
    }

    /// Gives the item a name to show in place of its content's first line,
    /// or takes the name away with `None`. The name is trimmed; a blank one
    /// takes the name away too. The content is never changed.
    ///
    /// # Errors
    ///
    /// [`CoreError::ItemNotFound`] if no item has this id.
    pub fn set_title(&mut self, id: ItemId, title: Option<&str>) -> Result<(), CoreError> {
        let title = title.map(str::trim).filter(|title| !title.is_empty());
        let changed = self.conn.execute(
            "UPDATE items SET title = ?2 WHERE id = ?1",
            params![id.as_raw(), title],
        )?;
        expect_one(changed, id)
    }

    /// Deletes the item and its files. The content is overwritten in the
    /// database file and dropped from the WAL, so it cannot be recovered even
    /// with the key.
    ///
    /// # Errors
    ///
    /// [`CoreError::ItemNotFound`] if no item has this id.
    pub fn delete(&mut self, id: ItemId) -> Result<(), CoreError> {
        let changed = self
            .conn
            .execute("DELETE FROM items WHERE id = ?1", [id.as_raw()])?;
        expect_one(changed, id)?;
        self.purge_deleted()
    }

    /// The item's content as plain text, for "paste as plain text": the text
    /// without formatting, or the file paths one per line. `None` for an image.
    ///
    /// # Errors
    ///
    /// [`CoreError::ItemNotFound`] if no item has this id.
    pub fn plain_text(&self, id: ItemId) -> Result<Option<String>, CoreError> {
        let item = self.get(id)?.ok_or(CoreError::ItemNotFound(id))?;
        Ok(item.content.plain_text().map(Cow::into_owned))
    }

    /// Number of stored items.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn count(&self) -> Result<usize, CoreError> {
        let count: i64 = self
            .conn
            .query_row("SELECT count(*) FROM items", [], |row| row.get(0))?;
        usize::try_from(count).map_err(|_| CoreError::Storage("item count out of range".into()))
    }

    /// Makes deleted content unrecoverable from the files. `secure_delete`
    /// zeroes freed pages, but in WAL mode the zeroed pages go to the WAL,
    /// whose older frames still hold the deleted rows until they are reused.
    /// A truncating checkpoint writes the zeroed pages into the database and
    /// empties the WAL. Call after every delete; it is cheap because the WAL
    /// only holds the writes since the last call.
    fn purge_deleted(&self) -> Result<(), CoreError> {
        // Answers (busy, frames, checkpointed). Only another connection can
        // make it busy, and the store holds the only one.
        self.conn
            .query_row("PRAGMA wal_checkpoint(TRUNCATE)", [], |_| Ok(()))?;
        Ok(())
    }

    /// Turns a raw row into an item, loading its files if it has any.
    fn finish(&self, raw: RawItem) -> Result<ClipItem, CoreError> {
        let files = if raw.is_files() {
            self.files(raw.id(), None)?
        } else {
            Vec::new()
        };
        raw.into_item(files)
    }

    /// The item's files in clipboard order, at most `limit` of them.
    fn files(&self, item_id: i64, limit: Option<usize>) -> Result<Vec<FileRef>, CoreError> {
        // A negative limit means none in SQLite.
        let limit = limit.map_or(-1, |n| i64::try_from(n).unwrap_or(i64::MAX));
        let rows = self
            .conn
            .prepare_cached(
                "SELECT path, size_bytes FROM item_files WHERE item_id = ?1 \
                 ORDER BY position LIMIT ?2",
            )?
            .query_map([item_id, limit], codec::file_from_row)?
            .collect::<Result<Vec<_>, _>>()?;
        rows.into_iter()
            .map(|(path, size)| codec::file_ref(path, size))
            .collect()
    }
}

/// An item to store, with the fields the store otherwise sets itself.
struct NewItem {
    content: ItemContent,
    source_app: Option<SourceApp>,
    pinned: bool,
    created_at: Timestamp,
    last_copied_at: Timestamp,
}

/// Writes `item` (and its files) on top of the history. The caller owns the
/// transaction, so several items can be stored all or nothing.
fn insert_row(conn: &Connection, item: NewItem) -> Result<ClipItem, CoreError> {
    let NewItem {
        content,
        source_app,
        pinned,
        created_at,
        last_copied_at,
    } = item;
    content.validate()?;
    let hash = content.content_hash();
    let columns = ItemColumns::new(&content)?;

    let inserted = conn.execute(
        "INSERT INTO items (kind, content_hash, pinned, source_app_id, source_app_name, \
         created_at, last_copied_at, byte_size, text_plain, rich_format, rich_data, \
         image_format, image_width, image_height, image_data, thumbnail, copy_order) \
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, \
         (SELECT coalesce(max(copy_order), 0) + 1 FROM items))",
        params![
            columns.kind,
            hash.as_bytes().as_slice(),
            pinned,
            source_app.as_ref().map(|app| app.id.as_str()),
            source_app.as_ref().and_then(|app| app.name.as_deref()),
            created_at.as_millis(),
            last_copied_at.as_millis(),
            columns.byte_size,
            columns.text_plain,
            columns.rich_format,
            columns.rich_data,
            columns.image_format,
            columns.image_width,
            columns.image_height,
            columns.image_data,
            columns.thumbnail,
        ],
    );
    match inserted {
        Err(err) if is_unique_violation(&err) => return Err(CoreError::DuplicateContent),
        other => other?,
    };
    let id = conn.last_insert_rowid();

    if let ItemContent::Files(files) = &content {
        let mut stmt = conn.prepare_cached(
            "INSERT INTO item_files (item_id, position, path, size_bytes) \
             VALUES (?1, ?2, ?3, ?4)",
        )?;
        for (position, file) in files.iter().enumerate() {
            let size = file
                .size_bytes
                .map(|s| codec::to_i64(s, "file size"))
                .transpose()?;
            stmt.execute(params![
                id,
                codec::to_i64(position, "file position")?,
                file.path,
                size
            ])?;
        }
    }

    Ok(ClipItem {
        id: ItemId::from_raw(id),
        content,
        pinned,
        source_app,
        created_at,
        last_copied_at,
    })
}

/// Creates `path` (and its parent directories) if missing, so SQLite opens a
/// file that already has owner-only permissions on Unix. Its WAL and
/// shared-memory files inherit them.
fn create_private_file(path: &Path) -> Result<(), CoreError> {
    if let Some(dir) = path.parent().filter(|dir| !dir.as_os_str().is_empty()) {
        fs::create_dir_all(dir)?;
    }
    let mut options = fs::OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    match options.open(path) {
        Err(err) if err.kind() != io::ErrorKind::AlreadyExists => Err(err.into()),
        _ => Ok(()),
    }
}

/// Keys the connection and proves the key works.
fn unlock(conn: &Connection, key: &DatabaseKey) -> Result<(), CoreError> {
    conn.execute_batch(&key.unlock_sql())
        .map_err(|err| unlock_error(&err))?;

    // Plain SQLite ignores `PRAGMA key` silently and would store everything in
    // the clear. Only SQLCipher answers `cipher_version`.
    let cipher: Option<String> = conn
        .query_row("PRAGMA cipher_version", [], |row| row.get(0))
        .optional()?;
    if cipher.is_none_or(|version| version.is_empty()) {
        return Err(CoreError::EncryptionUnavailable);
    }

    // The first read decrypts page 1; with the wrong key it fails here.
    match conn.query_row("SELECT count(*) FROM sqlite_master", [], |row| {
        row.get::<_, i64>(0)
    }) {
        Ok(_) => Ok(()),
        Err(err) if err.sqlite_error_code() == Some(ErrorCode::NotADatabase) => {
            Err(CoreError::WrongKey)
        }
        Err(err) => Err(err.into()),
    }
}

/// The error for a failed `PRAGMA key`, by SQLite code only. rusqlite puts
/// the statement's text in some errors, and this statement holds the key in
/// hex; the apps log core errors.
fn unlock_error(err: &rusqlite::Error) -> CoreError {
    let reason = match err {
        rusqlite::Error::SqliteFailure(code, _)
        | rusqlite::Error::SqlInputError { error: code, .. } => code.to_string(),
        _ => "unexpected error".to_owned(),
    };
    CoreError::Storage(format!("unlocking the database failed: {reason}"))
}

fn configure(conn: &Connection) -> Result<(), CoreError> {
    conn.busy_timeout(BUSY_TIMEOUT)?;
    conn.pragma_update(None, "foreign_keys", true)?;
    // Overwrite freed pages so deleted or expired items do not survive in the file.
    conn.pragma_update(None, "secure_delete", true)?;
    // Keep temporary tables and indexes in memory, never in unencrypted temp files.
    conn.pragma_update(None, "temp_store", "MEMORY")?;
    // WAL keeps the UI responsive while the watcher writes. SQLCipher encrypts it too.
    // Falls back to the default journal where WAL is unsupported.
    let _mode: String =
        conn.pragma_update_and_check(None, "journal_mode", "WAL", |row| row.get(0))?;
    Ok(())
}

fn is_unique_violation(err: &rusqlite::Error) -> bool {
    matches!(
        err,
        rusqlite::Error::SqliteFailure(e, _)
            if e.extended_code == rusqlite::ffi::SQLITE_CONSTRAINT_UNIQUE
    )
}

fn expect_one(changed: usize, id: ItemId) -> Result<(), CoreError> {
    if changed == 0 {
        Err(CoreError::ItemNotFound(id))
    } else {
        Ok(())
    }
}

#[cfg(test)]
mod extras_tests;
#[cfg(test)]
mod history_tests;
#[cfg(test)]
mod rules_tests;
#[cfg(test)]
mod tests;
