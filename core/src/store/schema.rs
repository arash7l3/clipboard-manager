//! Database schema and forward-only migrations, tracked in `PRAGMA user_version`.

use rusqlite::Connection;

use crate::error::CoreError;

/// Migration `i` upgrades the schema from version `i` to `i + 1`.
/// Append new migrations; never edit a shipped one.
const MIGRATIONS: &[&str] = &[V1];

/// Version 1: history items, with per-kind columns and their files.
///
/// Enum columns use the stable codes in `codec.rs`. CHECK constraints keep each
/// row consistent with its kind so a bad write fails loudly instead of loading
/// as a corrupt item later.
const V1: &str = "
CREATE TABLE items (
    id              INTEGER PRIMARY KEY,
    kind            INTEGER NOT NULL CHECK (kind IN (0, 1, 2)),
    content_hash    BLOB    NOT NULL UNIQUE CHECK (length(content_hash) = 32),
    pinned          INTEGER NOT NULL DEFAULT 0 CHECK (pinned IN (0, 1)),
    source_app_id   TEXT,
    source_app_name TEXT,
    created_at      INTEGER NOT NULL,
    last_copied_at  INTEGER NOT NULL,
    byte_size       INTEGER NOT NULL CHECK (byte_size >= 0),
    text_plain      TEXT,
    rich_format     INTEGER CHECK (rich_format IN (0, 1)),
    rich_data       TEXT,
    image_format    INTEGER CHECK (image_format IN (0, 1, 2, 3)),
    image_width     INTEGER CHECK (image_width > 0),
    image_height    INTEGER CHECK (image_height > 0),
    image_data      BLOB,
    thumbnail       BLOB,
    CHECK (source_app_id IS NOT NULL OR source_app_name IS NULL),
    CHECK ((kind = 0) = (text_plain IS NOT NULL)),
    CHECK ((rich_format IS NULL) = (rich_data IS NULL)),
    CHECK (kind = 0 OR rich_format IS NULL),
    CHECK ((kind = 1) = (image_data IS NOT NULL)),
    CHECK ((image_data IS NULL) = (image_format IS NULL)),
    CHECK ((image_data IS NULL) = (image_width IS NULL)),
    CHECK ((image_data IS NULL) = (image_height IS NULL)),
    CHECK (kind = 1 OR thumbnail IS NULL)
) STRICT;

CREATE INDEX items_by_recency ON items (last_copied_at DESC, id DESC);

CREATE TABLE item_files (
    item_id    INTEGER NOT NULL REFERENCES items (id) ON DELETE CASCADE,
    position   INTEGER NOT NULL CHECK (position >= 0),
    path       TEXT    NOT NULL CHECK (path <> ''),
    size_bytes INTEGER CHECK (size_bytes >= 0),
    PRIMARY KEY (item_id, position)
) STRICT, WITHOUT ROWID;
";

/// Newest schema version this build understands.
pub(super) fn latest_version() -> i64 {
    i64::try_from(MIGRATIONS.len()).expect("migration count fits in i64")
}

/// Brings the database up to [`latest_version`], one transaction per step.
pub(super) fn migrate(conn: &mut Connection) -> Result<(), CoreError> {
    let found: i64 = conn.pragma_query_value(None, "user_version", |row| row.get(0))?;
    let supported = latest_version();
    if found > supported {
        return Err(CoreError::UnsupportedSchema { found, supported });
    }
    let start =
        usize::try_from(found).map_err(|_| CoreError::Storage("negative schema version".into()))?;
    for (version, sql) in (found + 1..).zip(&MIGRATIONS[start..]) {
        let tx = conn.transaction()?;
        tx.execute_batch(sql)?;
        tx.pragma_update(None, "user_version", version)?;
        tx.commit()?;
    }
    Ok(())
}
