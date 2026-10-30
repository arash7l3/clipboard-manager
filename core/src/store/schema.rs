//! Database schema and forward-only migrations, tracked in `PRAGMA user_version`.

use rusqlite::Connection;

use crate::error::CoreError;

/// Migration `i` upgrades the schema from version `i` to `i + 1`.
/// Append new migrations; never edit a shipped one.
const MIGRATIONS: &[&str] = &[V1, V2];

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

/// Version 2: history is ordered by `copy_order`, which ranks items in the
/// order their copies were recorded. Ordering by `last_copied_at` put new
/// copies below older ones, or evicted them at once, when the clock went back.
/// Existing rows are ranked by their old order.
const V2: &str = "
ALTER TABLE items ADD COLUMN copy_order INTEGER NOT NULL DEFAULT 0;

UPDATE items SET copy_order = (
    SELECT count(*) FROM items AS other
    WHERE other.last_copied_at < items.last_copied_at
       OR (other.last_copied_at = items.last_copied_at AND other.id <= items.id)
);

DROP INDEX items_by_recency;
CREATE UNIQUE INDEX items_by_copy_order ON items (copy_order);
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

#[cfg(test)]
mod tests {
    use rusqlite::Connection;
    use tempfile::TempDir;

    use super::*;
    use crate::key::DatabaseKey;
    use crate::model::{ItemContent, TextContent, Timestamp};
    use crate::store::{unlock, Store};

    #[test]
    fn v2_keeps_the_order_of_existing_items() {
        let dir = TempDir::new().unwrap();
        let path = dir.path().join("history.db");
        let key = DatabaseKey::generate().unwrap();
        let conn = Connection::open(&path).unwrap();
        unlock(&conn, &key).unwrap();
        conn.execute_batch(V1).unwrap();
        conn.pragma_update(None, "user_version", 1).unwrap();
        // (id, last_copied_at). Items 2 and 3 tie, so the higher id is newer.
        for (id, copied) in [(1, 30), (2, 10), (3, 10), (4, 20)] {
            conn.execute(
                "INSERT INTO items (id, kind, content_hash, created_at, last_copied_at, \
                 byte_size, text_plain) VALUES (?1, 0, randomblob(32), ?2, ?2, 1, ?1)",
                [id, copied],
            )
            .unwrap();
        }
        drop(conn);

        let mut store = Store::open(&path, &key).unwrap();
        let order = |store: &Store| {
            store
                .list()
                .unwrap()
                .iter()
                .map(|item| item.id.as_raw())
                .collect::<Vec<_>>()
        };
        assert_eq!(order(&store), [1, 4, 3, 2]);

        // New copies go on top of the migrated items.
        let content = ItemContent::Text(TextContent {
            plain: "new".into(),
            rich: None,
        });
        let new = store
            .insert(content, None, Timestamp::from_millis(0))
            .unwrap();
        store.touch(new.id, Timestamp::from_millis(0)).unwrap();
        assert_eq!(order(&store), [new.id.as_raw(), 1, 4, 3, 2]);
    }
}
