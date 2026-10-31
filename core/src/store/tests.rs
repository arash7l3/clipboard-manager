use std::cell::RefCell;
use std::fs;

use tempfile::TempDir;

use super::*;
use crate::error::InvalidContent;
use crate::key::{KeyStoreError, KEY_LEN};
use crate::model::{ImageContent, ImageFormat, RichText, RichTextFormat, TextContent};

const SECRET: &str = "correct-horse-battery-staple-7f3a";

/// In-memory stand-in for the Keychain / Credential Manager.
#[derive(Default)]
struct MemoryKeyStore {
    key: RefCell<Option<Vec<u8>>>,
    fail_load: bool,
    fail_save: bool,
}

impl KeyStore for MemoryKeyStore {
    fn load(&self) -> Result<Option<Vec<u8>>, KeyStoreError> {
        if self.fail_load {
            return Err(KeyStoreError::new("load failed"));
        }
        Ok(self.key.borrow().clone())
    }

    fn save(&self, key: &[u8]) -> Result<(), KeyStoreError> {
        if self.fail_save {
            return Err(KeyStoreError::new("save failed"));
        }
        *self.key.borrow_mut() = Some(key.to_vec());
        Ok(())
    }
}

pub(super) struct Fixture {
    _dir: TempDir,
    pub(super) path: PathBuf,
    key: DatabaseKey,
}

impl Fixture {
    pub(super) fn new() -> Self {
        let dir = TempDir::new().unwrap();
        let path = dir.path().join("nested").join("history.db");
        Self {
            _dir: dir,
            path,
            key: DatabaseKey::generate().unwrap(),
        }
    }

    pub(super) fn open(&self) -> Store {
        Store::open(&self.path, &self.key).unwrap()
    }

    fn wal_path(&self) -> PathBuf {
        let mut wal = self.path.as_os_str().to_owned();
        wal.push("-wal");
        PathBuf::from(wal)
    }

    pub(super) fn file_bytes(&self) -> Vec<u8> {
        let mut bytes = fs::read(&self.path).unwrap();
        if let Ok(more) = fs::read(self.wal_path()) {
            bytes.extend(more);
        }
        bytes
    }

    /// Size of the WAL, which holds every page written since the last checkpoint.
    pub(super) fn wal_len(&self) -> u64 {
        fs::metadata(self.wal_path()).map_or(0, |m| m.len())
    }
}

pub(super) fn at(millis: i64) -> Timestamp {
    Timestamp::from_millis(millis)
}

pub(super) fn text(plain: &str) -> ItemContent {
    ItemContent::Text(TextContent {
        plain: plain.into(),
        rich: None,
    })
}

pub(super) fn rich_text() -> ItemContent {
    ItemContent::Text(TextContent {
        plain: "سلام world".into(),
        rich: Some(RichText {
            format: RichTextFormat::Html,
            data: "<b>سلام</b> world".into(),
        }),
    })
}

pub(super) fn image(thumbnail: Option<Vec<u8>>) -> ItemContent {
    ItemContent::Image(ImageContent {
        format: ImageFormat::Tiff,
        width: 640,
        height: 480,
        data: vec![7; 4096],
        thumbnail,
    })
}

pub(super) fn files() -> ItemContent {
    ItemContent::Files(vec![
        FileRef {
            path: "/Users/me/b.pdf".into(),
            size_bytes: Some(1_234_567),
        },
        FileRef {
            path: "/Users/me/a folder".into(),
            size_bytes: None,
        },
    ])
}

pub(super) fn app() -> SourceApp {
    SourceApp {
        id: "com.apple.Safari".into(),
        name: Some("Safari".into()),
    }
}

// --- Encryption ---

#[test]
fn file_on_disk_is_encrypted() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.insert(text(SECRET), Some(app()), at(1)).unwrap();
    drop(store);

    let bytes = fx.file_bytes();
    assert!(
        !bytes.starts_with(b"SQLite format 3\0"),
        "plaintext SQLite header"
    );
    let leaked = |needle: &[u8]| bytes.windows(needle.len()).any(|w| w == needle);
    assert!(!leaked(SECRET.as_bytes()), "content readable on disk");
    assert!(!leaked(b"com.apple.Safari"), "metadata readable on disk");
    assert!(!leaked(b"CREATE TABLE"), "schema readable on disk");
}

#[test]
fn sqlcipher_is_linked() {
    let fx = Fixture::new();
    let store = fx.open();
    let version: String = store
        .conn
        .query_row("PRAGMA cipher_version", [], |row| row.get(0))
        .unwrap();
    assert!(version.starts_with('4'), "unexpected SQLCipher {version}");
}

#[test]
fn reopens_with_same_key() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = store.insert(text(SECRET), None, at(1)).unwrap();
    drop(store);

    let store = fx.open();
    assert_eq!(store.get(item.id).unwrap(), Some(item));
}

#[test]
fn wrong_key_is_rejected() {
    let fx = Fixture::new();
    fx.open().insert(text("x"), None, at(1)).unwrap();

    let other = DatabaseKey::generate().unwrap();
    assert_eq!(
        Store::open(&fx.path, &other).err(),
        Some(CoreError::WrongKey)
    );
    // The original key still works: a failed open must not damage the file.
    assert_eq!(fx.open().count().unwrap(), 1);
}

#[test]
fn non_database_file_is_rejected() {
    let fx = Fixture::new();
    fs::create_dir_all(fx.path.parent().unwrap()).unwrap();
    fs::write(&fx.path, vec![0x5a; 8192]).unwrap();
    assert_eq!(
        Store::open(&fx.path, &fx.key).err(),
        Some(CoreError::WrongKey)
    );
}

#[cfg(unix)]
#[test]
fn new_file_is_owner_only() {
    use std::os::unix::fs::PermissionsExt;
    let fx = Fixture::new();
    fx.open();
    let mode = fs::metadata(&fx.path).unwrap().permissions().mode();
    assert_eq!(mode & 0o777, 0o600);
}

#[test]
fn newer_schema_is_refused() {
    let fx = Fixture::new();
    let store = fx.open();
    store.conn.pragma_update(None, "user_version", 99).unwrap();
    drop(store);
    assert_eq!(
        Store::open(&fx.path, &fx.key).err(),
        Some(CoreError::UnsupportedSchema {
            found: 99,
            supported: schema::latest_version(),
        })
    );
}

#[test]
fn delete_database_removes_all_files() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.insert(text("x"), None, at(1)).unwrap();
    drop(store);

    Store::delete_database(&fx.path).unwrap();
    let leftovers = fs::read_dir(fx.path.parent().unwrap()).unwrap().count();
    assert_eq!(leftovers, 0);
    // Deleting again is a no-op.
    Store::delete_database(&fx.path).unwrap();
    assert_eq!(fx.open().count().unwrap(), 0);
}

// --- Key store flow ---

#[test]
fn first_run_generates_and_saves_key() {
    let fx = Fixture::new();
    let keys = MemoryKeyStore::default();
    let mut store = Store::open_with_key_store(&fx.path, &keys).unwrap();
    store.insert(text("x"), None, at(1)).unwrap();
    drop(store);

    let saved = keys.key.borrow().clone().unwrap();
    assert_eq!(saved.len(), KEY_LEN);
    let key = DatabaseKey::from_bytes(&saved).unwrap();
    assert_eq!(Store::open(&fx.path, &key).unwrap().count().unwrap(), 1);
    // Later runs reuse the saved key.
    let store = Store::open_with_key_store(&fx.path, &keys).unwrap();
    assert_eq!(store.count().unwrap(), 1);
    assert_eq!(keys.key.borrow().as_deref(), Some(saved.as_slice()));
}

#[test]
fn missing_key_for_existing_database_is_an_error() {
    let fx = Fixture::new();
    fx.open().insert(text("x"), None, at(1)).unwrap();

    let keys = MemoryKeyStore::default();
    assert_eq!(
        Store::open_with_key_store(&fx.path, &keys).err(),
        Some(CoreError::KeyMissing)
    );
    assert!(
        keys.key.borrow().is_none(),
        "must not save a replacement key"
    );
    assert_eq!(fx.open().count().unwrap(), 1);
}

#[test]
fn key_store_failures_propagate() {
    let fx = Fixture::new();
    let failing_load = MemoryKeyStore {
        fail_load: true,
        ..MemoryKeyStore::default()
    };
    assert_eq!(
        Store::open_with_key_store(&fx.path, &failing_load).err(),
        Some(CoreError::KeyStore(KeyStoreError::new("load failed")))
    );

    let failing_save = MemoryKeyStore {
        fail_save: true,
        ..MemoryKeyStore::default()
    };
    assert_eq!(
        Store::open_with_key_store(&fx.path, &failing_save).err(),
        Some(CoreError::KeyStore(KeyStoreError::new("save failed")))
    );
    assert!(!fx.path.exists(), "database created without a saved key");
}

#[test]
fn malformed_stored_key_is_rejected() {
    let fx = Fixture::new();
    let keys = MemoryKeyStore::default();
    *keys.key.borrow_mut() = Some(vec![1; 16]);
    assert_eq!(
        Store::open_with_key_store(&fx.path, &keys).err(),
        Some(CoreError::InvalidKey)
    );
}

// --- Items ---

#[test]
fn round_trips_every_kind() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let contents = [
        text(SECRET),
        rich_text(),
        image(None),
        image(Some(vec![1, 2, 3])),
        files(),
    ];
    // Two images with the same data are duplicates; vary the second one.
    let mut contents = contents.to_vec();
    if let ItemContent::Image(img) = &mut contents[3] {
        img.data = vec![8; 10];
        img.format = ImageFormat::Png;
    }

    for (i, content) in contents.into_iter().enumerate() {
        let source = (i % 2 == 0).then(app);
        let millis = i64::try_from(i).unwrap() + 100;
        let item = store
            .insert(content.clone(), source.clone(), at(millis))
            .unwrap();
        assert_eq!(item.content, content);
        assert_eq!(item.source_app, source);
        assert!(!item.pinned);
        assert_eq!(item.created_at, at(millis));
        assert_eq!(item.last_copied_at, at(millis));
        assert_eq!(store.get(item.id).unwrap(), Some(item));
    }
    drop(store);

    // And again after a reopen, straight from disk.
    let store = fx.open();
    let listed = store.list().unwrap();
    assert_eq!(listed.len(), 5);
    assert_eq!(listed[0].content, files());
    assert_eq!(listed[4].content, text(SECRET));
}

#[test]
fn source_app_without_name_round_trips() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let source = SourceApp {
        id: r"C:\Program Files\App\app.exe".into(),
        name: None,
    };
    let item = store.insert(text("x"), Some(source), at(1)).unwrap();
    assert_eq!(store.get(item.id).unwrap(), Some(item));
}

#[test]
fn invalid_content_is_not_stored() {
    let fx = Fixture::new();
    let mut store = fx.open();
    assert_eq!(
        store.insert(text(""), None, at(1)).err(),
        Some(CoreError::InvalidContent(InvalidContent::EmptyText))
    );
    assert_eq!(
        store.insert(ItemContent::Files(vec![]), None, at(1)).err(),
        Some(CoreError::InvalidContent(InvalidContent::NoFiles))
    );
    assert_eq!(store.count().unwrap(), 0);
}

#[test]
fn duplicate_content_is_rejected_and_findable() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = store.insert(text("same"), None, at(1)).unwrap();
    assert_eq!(
        store.insert(text("same"), Some(app()), at(2)).err(),
        Some(CoreError::DuplicateContent)
    );
    assert_eq!(store.count().unwrap(), 1);

    assert_eq!(
        store.find_by_hash(&text("same").content_hash()).unwrap(),
        Some(item.id)
    );
    assert_eq!(
        store.find_by_hash(&text("other").content_hash()).unwrap(),
        None
    );
}

#[test]
fn list_is_newest_first_and_touch_moves_to_top() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let a = store.insert(text("a"), None, at(10)).unwrap();
    let b = store.insert(text("b"), None, at(20)).unwrap();
    // Same timestamp as `b`: the later insert wins the tie.
    let c = store.insert(text("c"), None, at(20)).unwrap();
    let ids = |store: &Store| {
        store
            .list()
            .unwrap()
            .iter()
            .map(|i| i.id)
            .collect::<Vec<_>>()
    };
    assert_eq!(ids(&store), [c.id, b.id, a.id]);

    store.touch(a.id, at(30)).unwrap();
    assert_eq!(ids(&store), [a.id, c.id, b.id]);
    let touched = store.get(a.id).unwrap().unwrap();
    assert_eq!(touched.created_at, at(10));
    assert_eq!(touched.last_copied_at, at(30));
}

#[test]
fn pin_persists() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = store.insert(text("x"), None, at(1)).unwrap();
    store.set_pinned(item.id, true).unwrap();
    drop(store);

    let mut store = fx.open();
    assert!(store.get(item.id).unwrap().unwrap().pinned);
    store.set_pinned(item.id, false).unwrap();
    assert!(!store.get(item.id).unwrap().unwrap().pinned);
}

#[test]
fn delete_removes_item_and_its_files() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = store.insert(files(), None, at(1)).unwrap();
    store.delete(item.id).unwrap();

    assert_eq!(store.get(item.id).unwrap(), None);
    assert_eq!(store.count().unwrap(), 0);
    let file_rows: i64 = store
        .conn
        .query_row("SELECT count(*) FROM item_files", [], |row| row.get(0))
        .unwrap();
    assert_eq!(file_rows, 0);
    // The content can be stored again once deleted.
    store.insert(files(), None, at(2)).unwrap();
}

#[test]
fn deleting_empties_the_wal() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = store.insert(text(SECRET), None, at(1)).unwrap();
    assert!(fx.wal_len() > 0, "test needs WAL mode");

    store.delete(item.id).unwrap();
    // The WAL frames that held the item are gone, not just superseded.
    assert_eq!(fx.wal_len(), 0);
}

#[test]
fn editing_a_snippet_empties_the_wal() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let snippet = store.add_snippet("title", SECRET, at(1)).unwrap();
    assert!(fx.wal_len() > 0, "test needs WAL mode");

    store
        .update_snippet(snippet.id, "title", "new body", at(2))
        .unwrap();
    // The WAL frames that held the old text are gone, not just superseded.
    assert_eq!(fx.wal_len(), 0);
}

#[test]
fn unlock_errors_never_carry_the_key() {
    let key = DatabaseKey::from_bytes(&[0xAB; KEY_LEN]).unwrap();
    let hex = "AB".repeat(KEY_LEN);
    // rusqlite quotes the statement in errors about a token in it.
    let err = rusqlite::Error::SqlInputError {
        error: rusqlite::ffi::Error::new(rusqlite::ffi::SQLITE_ERROR),
        msg: "near \"x\": syntax error".into(),
        sql: key.unlock_sql().to_string(),
        offset: 13,
    };
    assert!(err.to_string().contains(&hex), "test needs a quoting error");

    let message = unlock_error(&err).to_string();
    assert!(!message.contains(&hex), "leaked: {message}");
    assert!(message.starts_with("storage error: unlocking the database failed: "));
}

#[test]
fn missing_item_operations_report_not_found() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let missing = ItemId::from_raw(42);
    assert_eq!(store.get(missing).unwrap(), None);
    assert_eq!(
        store.touch(missing, at(1)).err(),
        Some(CoreError::ItemNotFound(missing))
    );
    assert_eq!(
        store.set_pinned(missing, true).err(),
        Some(CoreError::ItemNotFound(missing))
    );
    assert_eq!(
        store.delete(missing).err(),
        Some(CoreError::ItemNotFound(missing))
    );
}

#[test]
fn schema_rejects_inconsistent_rows() {
    let fx = Fixture::new();
    let store = fx.open();
    // A text row without text breaks a CHECK constraint.
    let result = store.conn.execute(
        "INSERT INTO items (kind, content_hash, created_at, last_copied_at, byte_size) \
         VALUES (0, zeroblob(32), 1, 1, 0)",
        [],
    );
    assert!(result.is_err());
}

#[test]
fn corrupt_row_loads_as_error_without_content() {
    let fx = Fixture::new();
    let store = fx.open();
    // A file item with no file rows passes the CHECKs but is not a valid item.
    store
        .conn
        .execute(
            "INSERT INTO items (id, kind, content_hash, created_at, last_copied_at, byte_size) \
             VALUES (7, 2, zeroblob(32), 1, 1, 0)",
            [],
        )
        .unwrap();
    let err = store.get(ItemId::from_raw(7)).unwrap_err();
    assert!(matches!(&err, CoreError::Storage(msg) if msg.contains("file item without files")));
    assert!(store.list().is_err());
}

#[test]
fn errors_do_not_echo_content() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.insert(text(SECRET), None, at(1)).unwrap();
    let err = store.insert(text(SECRET), None, at(2)).unwrap_err();
    assert!(!err.to_string().contains(SECRET));
    assert!(!format!("{err:?}").contains(SECRET));
}
