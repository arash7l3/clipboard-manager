//! Tests through the exported API, as the apps call it.

use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use tempfile::TempDir;

use super::*;

/// In-memory stand-in for the Keychain / Credential Manager.
#[derive(Clone, Default)]
struct MemoryKeys {
    key: Arc<Mutex<Option<Vec<u8>>>>,
    fail: bool,
}

impl KeyStore for MemoryKeys {
    fn load(&self) -> Result<Option<Vec<u8>>, KeyStoreError> {
        if self.fail {
            return Err(KeyStoreError::Failed {
                message: "OSStatus -25308".into(),
            });
        }
        Ok(self.key.lock().unwrap().clone())
    }

    fn save(&self, key: Vec<u8>) -> Result<(), KeyStoreError> {
        *self.key.lock().unwrap() = Some(key);
        Ok(())
    }
}

struct Fixture {
    _dir: TempDir,
    path: PathBuf,
    keys: MemoryKeys,
    store: Arc<ClipStore>,
}

impl Fixture {
    fn new() -> Self {
        let dir = TempDir::new().unwrap();
        let path = dir.path().join("history.db");
        let keys = MemoryKeys::default();
        let store = ClipStore::open(path.to_str().unwrap(), Box::new(keys.clone())).unwrap();
        Self {
            _dir: dir,
            path,
            keys,
            store,
        }
    }

    fn path(&self) -> &str {
        self.path.to_str().unwrap()
    }
}

fn at(millis: u64) -> SystemTime {
    UNIX_EPOCH + Duration::from_millis(millis)
}

fn text(plain: &str) -> ItemContent {
    ItemContent::Text {
        text: TextContent {
            plain: plain.into(),
            rich: None,
        },
    }
}

fn capture(content: ItemContent, millis: u64) -> Capture {
    Capture {
        content,
        source_app: None,
        captured_at: at(millis),
        concealed: false,
    }
}

fn added(recorded: Recorded) -> ClipItem {
    match recorded {
        Recorded::Added { item } => item,
        _ => panic!("expected a new item"),
    }
}

fn skipped(recorded: &Recorded) -> SkipReason {
    match recorded {
        Recorded::Skipped { reason } => *reason,
        _ => panic!("expected a skip"),
    }
}

fn plain(item: &ClipItem) -> &str {
    match &item.content {
        ItemContent::Text { text } => &text.plain,
        _ => panic!("expected text"),
    }
}

fn all() -> Query {
    Query {
        category: Category::All,
        text: String::new(),
        limit: None,
    }
}

fn open_err(path: &str, keys: MemoryKeys) -> ClipoError {
    match ClipStore::open(path, Box::new(keys)) {
        Ok(_) => panic!("open should fail"),
        Err(err) => err,
    }
}

#[test]
fn records_and_lists_items() {
    let fx = Fixture::new();
    let first = added(fx.store.record(capture(text("one"), 1_000)).unwrap());
    added(fx.store.record(capture(text("two"), 2_000)).unwrap());

    assert_eq!(first.created_at, at(1_000));
    assert_eq!(first.last_copied_at, at(1_000));
    assert!(!first.pinned);
    assert_eq!(fx.store.count().unwrap(), 2);

    let items = fx.store.search(all()).unwrap();
    let texts: Vec<_> = items.iter().map(plain).collect();
    assert_eq!(texts, ["two", "one"]);

    let got = fx.store.get(first.id).unwrap().unwrap();
    assert!(got == first);
    assert!(fx.store.get(first.id + 100).unwrap().is_none());
}

#[test]
fn repeated_copy_bumps_existing_item() {
    let fx = Fixture::new();
    let item = added(fx.store.record(capture(text("same"), 1_000)).unwrap());
    let Recorded::Bumped { item: bumped } = fx.store.record(capture(text("same"), 5_000)).unwrap()
    else {
        panic!("expected a bump");
    };
    assert_eq!(bumped.id, item.id);
    assert_eq!(bumped.created_at, at(1_000));
    assert_eq!(bumped.last_copied_at, at(5_000));
    assert_eq!(fx.store.count().unwrap(), 1);
}

#[test]
fn every_content_kind_round_trips() {
    let fx = Fixture::new();
    let contents = [
        ItemContent::Text {
            text: TextContent {
                plain: "bold".into(),
                rich: Some(RichText {
                    format: RichTextFormat::Html,
                    data: "<b>bold</b>".into(),
                }),
            },
        },
        ItemContent::Image {
            image: ImageContent {
                format: ImageFormat::Tiff,
                width: 3,
                height: 2,
                data: vec![1, 2, 3],
                thumbnail: Some(vec![9]),
            },
        },
        ItemContent::Files {
            files: vec![
                FileRef {
                    path: "/Users/a/دفترچه.txt".into(),
                    size_bytes: Some(12),
                },
                FileRef {
                    path: "/Users/a/folder".into(),
                    size_bytes: None,
                },
            ],
        },
    ];
    for (n, content) in contents.into_iter().enumerate() {
        let mut capture = capture(content.clone(), 1_000 + u64::try_from(n).unwrap());
        capture.source_app = Some(SourceApp {
            id: "com.example.app".into(),
            name: Some("Example".into()),
        });
        let item = added(fx.store.record(capture).unwrap());
        let stored = fx.store.get(item.id).unwrap().unwrap();
        assert!(stored.content == content);
        assert_eq!(
            stored.source_app.as_ref().map(|app| app.id.as_str()),
            Some("com.example.app")
        );
    }
}

#[test]
fn search_filters_by_tab_text_and_limit() {
    let fx = Fixture::new();
    added(fx.store.record(capture(text("كتاب خوب"), 1_000)).unwrap());
    let pinned = added(fx.store.record(capture(text("book"), 2_000)).unwrap());
    added(fx.store.record(capture(text("کتاب بد"), 3_000)).unwrap());
    fx.store.set_pinned(pinned.id, true).unwrap();

    let found = fx
        .store
        .search(Query {
            text: "کتاب".into(),
            ..all()
        })
        .unwrap();
    assert_eq!(found.len(), 2);

    let limited = fx
        .store
        .search(Query {
            text: "کتاب".into(),
            limit: Some(1),
            ..all()
        })
        .unwrap();
    assert_eq!(limited.iter().map(plain).collect::<Vec<_>>(), ["کتاب بد"]);

    let pins = fx
        .store
        .search(Query {
            category: Category::Pinned,
            ..all()
        })
        .unwrap();
    assert_eq!(pins.iter().map(plain).collect::<Vec<_>>(), ["book"]);

    let images = fx
        .store
        .search(Query {
            category: Category::Images,
            ..all()
        })
        .unwrap();
    assert!(images.is_empty());
}

#[test]
fn previews_list_what_the_popup_shows() {
    let fx = Fixture::new();
    let link = added(
        fx.store
            .record(capture(text("https://example.com"), 1_000))
            .unwrap(),
    );
    let files: Vec<_> = (0..12)
        .map(|n| FileRef {
            path: format!("/tmp/{n}"),
            size_bytes: Some(n),
        })
        .collect();
    added(
        fx.store
            .record(capture(ItemContent::Files { files }, 2_000))
            .unwrap(),
    );
    fx.store.set_pinned(link.id, true).unwrap();

    let previews = fx.store.previews(all()).unwrap();
    assert_eq!(previews.len(), 2);
    match &previews[0].content {
        PreviewContent::Files { files, total } => {
            assert_eq!(files.len(), 10);
            assert_eq!(*total, 12);
        }
        _ => panic!("files first"),
    }
    let row = &previews[1];
    assert_eq!(row.id, link.id);
    assert!(row.pinned);
    assert_eq!(row.last_copied_at, at(1_000));
    assert!(matches!(
        &row.content,
        PreviewContent::Text { snippet, truncated: false, hint: TextHint::Link }
            if snippet == "https://example.com"
    ));

    let pinned = fx
        .store
        .previews(Query {
            category: Category::Pinned,
            ..all()
        })
        .unwrap();
    assert_eq!(pinned.iter().map(|p| p.id).collect::<Vec<_>>(), [link.id]);
}

#[test]
fn touch_pin_and_delete() {
    let fx = Fixture::new();
    let old = added(fx.store.record(capture(text("old"), 1_000)).unwrap());
    added(fx.store.record(capture(text("new"), 2_000)).unwrap());

    fx.store.touch(old.id, at(3_000)).unwrap();
    assert_eq!(plain(&fx.store.search(all()).unwrap()[0]), "old");

    fx.store.set_pinned(old.id, true).unwrap();
    assert!(fx.store.get(old.id).unwrap().unwrap().pinned);

    fx.store.delete(old.id).unwrap();
    assert_eq!(fx.store.count().unwrap(), 1);
    assert_eq!(
        fx.store.delete(old.id),
        Err(ClipoError::ItemNotFound { id: old.id })
    );
    assert_eq!(
        fx.store.touch(old.id, at(4_000)),
        Err(ClipoError::ItemNotFound { id: old.id })
    );
    assert_eq!(
        fx.store.set_pinned(old.id, false),
        Err(ClipoError::ItemNotFound { id: old.id })
    );
}

#[test]
fn limits_evict_and_reject_zero() {
    let fx = Fixture::new();
    assert_eq!(fx.store.limits(), default_history_limits());
    assert_eq!(default_history_limits().max_items, 200);

    for n in 0..3 {
        added(fx.store.record(capture(text(&format!("t{n}")), n)).unwrap());
    }
    fx.store
        .set_limits(HistoryLimits {
            max_items: 2,
            max_item_bytes: 4,
        })
        .unwrap();
    assert_eq!(fx.store.count().unwrap(), 2);
    assert_eq!(
        skipped(&fx.store.record(capture(text("too long"), 10)).unwrap()),
        SkipReason::TooLarge
    );

    let zero = HistoryLimits {
        max_items: 0,
        max_item_bytes: 1,
    };
    assert_eq!(fx.store.set_limits(zero), Err(ClipoError::InvalidLimits));
    // Huge values saturate instead of failing.
    let huge = HistoryLimits {
        max_items: u64::MAX,
        max_item_bytes: u64::MAX,
    };
    fx.store.set_limits(huge).unwrap();
    assert_eq!(fx.store.limits(), huge);
}

#[test]
fn rules_skip_captures() {
    let fx = Fixture::new();
    assert_eq!(
        fx.store.rules(),
        CaptureRules {
            private_mode: false,
            ignored_apps: vec![],
            auto_clear_after: None
        }
    );

    let mut concealed = capture(text("secret"), 1);
    concealed.concealed = true;
    assert_eq!(
        skipped(&fx.store.record(concealed).unwrap()),
        SkipReason::Concealed
    );

    fx.store
        .set_rules(CaptureRules {
            private_mode: false,
            ignored_apps: vec!["com.Example.Passwords".into()],
            auto_clear_after: None,
        })
        .unwrap();
    let mut ignored = capture(text("pw"), 2);
    ignored.source_app = Some(SourceApp {
        id: "com.example.passwords".into(),
        name: None,
    });
    assert_eq!(
        skipped(&fx.store.record(ignored).unwrap()),
        SkipReason::IgnoredApp
    );

    fx.store.set_private_mode(true);
    assert!(fx.store.rules().private_mode);
    assert_eq!(fx.store.rules().ignored_apps.len(), 1);
    assert_eq!(
        skipped(&fx.store.record(capture(text("hidden"), 3)).unwrap()),
        SkipReason::PrivateMode
    );
    assert_eq!(fx.store.count().unwrap(), 0);
}

#[test]
fn auto_clear_deletes_old_unpinned_items() {
    let fx = Fixture::new();
    let old = added(fx.store.record(capture(text("old"), 1_000)).unwrap());
    let kept = added(fx.store.record(capture(text("pinned"), 1_000)).unwrap());
    added(fx.store.record(capture(text("new"), 9_000)).unwrap());
    fx.store.set_pinned(kept.id, true).unwrap();

    assert_eq!(fx.store.clear_expired(at(10_000)).unwrap(), 0);

    let rules = CaptureRules {
        auto_clear_after: Some(Duration::from_secs(5)),
        ..fx.store.rules()
    };
    fx.store.set_rules(rules).unwrap();
    assert_eq!(fx.store.clear_expired(at(10_000)).unwrap(), 1);
    assert!(fx.store.get(old.id).unwrap().is_none());
    assert_eq!(fx.store.count().unwrap(), 2);

    let zero = CaptureRules {
        auto_clear_after: Some(Duration::ZERO),
        ..fx.store.rules()
    };
    assert_eq!(fx.store.set_rules(zero), Err(ClipoError::InvalidRules));
}

#[test]
fn invalid_content_is_reported() {
    let fx = Fixture::new();
    let err = fx.store.record(capture(text(""), 1)).err();
    assert_eq!(
        err,
        Some(ClipoError::InvalidContent {
            reason: InvalidContent::EmptyText
        })
    );
    let err = fx
        .store
        .record(capture(ItemContent::Files { files: vec![] }, 1))
        .err();
    assert_eq!(
        err,
        Some(ClipoError::InvalidContent {
            reason: InvalidContent::NoFiles
        })
    );
}

#[test]
fn reopens_with_saved_key() {
    let fx = Fixture::new();
    added(fx.store.record(capture(text("kept"), 1)).unwrap());
    assert_eq!(fx.keys.key.lock().unwrap().as_ref().map(Vec::len), Some(32));

    let Fixture {
        _dir, path, keys, ..
    } = fx;
    let reopened = ClipStore::open(path.to_str().unwrap(), Box::new(keys)).unwrap();
    assert_eq!(reopened.count().unwrap(), 1);
}

#[test]
fn key_problems_map_to_errors() {
    let fx = Fixture::new();

    let empty = MemoryKeys::default();
    assert_eq!(open_err(fx.path(), empty), ClipoError::KeyMissing);

    let wrong = MemoryKeys::default();
    *wrong.key.lock().unwrap() = Some(vec![7; 32]);
    assert_eq!(open_err(fx.path(), wrong), ClipoError::WrongKey);

    let short = MemoryKeys::default();
    *short.key.lock().unwrap() = Some(vec![7; 16]);
    assert_eq!(open_err(fx.path(), short), ClipoError::InvalidKey);

    let failing = MemoryKeys {
        fail: true,
        ..MemoryKeys::default()
    };
    assert_eq!(
        open_err(fx.path(), failing),
        ClipoError::KeyStore {
            message: "OSStatus -25308".into()
        }
    );
}

#[test]
fn delete_database_allows_a_fresh_start() {
    let fx = Fixture::new();
    added(fx.store.record(capture(text("gone"), 1)).unwrap());
    let Fixture {
        _dir, path, store, ..
    } = fx;
    drop(store);
    let path = path.to_str().unwrap();

    delete_database(path).unwrap();
    // A missing file is not an error.
    delete_database(path).unwrap();

    let fresh = ClipStore::open(path, Box::new(MemoryKeys::default())).unwrap();
    assert_eq!(fresh.count().unwrap(), 0);
}

#[test]
fn tabs_are_in_display_order() {
    assert_eq!(
        tab_categories(),
        [
            Category::All,
            Category::Text,
            Category::Images,
            Category::Files,
            Category::Pinned
        ]
    );
}

#[test]
fn store_is_shareable_between_threads() {
    let fx = Fixture::new();
    let handles: Vec<_> = (0..4)
        .map(|n| {
            let store = Arc::clone(&fx.store);
            std::thread::spawn(move || {
                for i in 0..10 {
                    store
                        .record(capture(text(&format!("{n}-{i}")), n * 100 + i))
                        .unwrap();
                }
            })
        })
        .collect();
    for handle in handles {
        handle.join().unwrap();
    }
    assert_eq!(fx.store.count().unwrap(), 40);
}

#[test]
fn error_messages_do_not_quote_content() {
    let err = ClipoError::from(clipo_core::CoreError::InvalidContent(
        clipo_core::InvalidContent::EmptyFilePath,
    ));
    assert_eq!(
        err.to_string(),
        "invalid clipboard content: file path is empty"
    );
    let callback = KeyStoreError::from(uniffi::UnexpectedUniFFICallbackError::new("boom"));
    assert_eq!(callback.to_string(), "unexpected key store error: boom");
}
