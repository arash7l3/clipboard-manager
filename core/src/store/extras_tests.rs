//! Snippets, plain text, and export / import.

use super::history_tests::{added, capture, texts};
use super::tests::{app, at, files, image, rich_text, text, Fixture};
use super::*;
use crate::error::InvalidSnippet;
use crate::model::SnippetId;

fn contains(haystack: &[u8], needle: &str) -> bool {
    haystack
        .windows(needle.len())
        .any(|window| window == needle.as_bytes())
}

// --- Plain text ---

#[test]
fn plain_text_drops_formatting() {
    let mut store = Fixture::new().open();
    let item = added(store.record(capture(rich_text(), 1)).unwrap());
    assert_eq!(
        store.plain_text(item.id).unwrap().as_deref(),
        Some("سلام world")
    );
}

#[test]
fn plain_text_of_files_is_one_path_per_line() {
    let mut store = Fixture::new().open();
    let item = added(store.record(capture(files(), 1)).unwrap());
    assert_eq!(
        store.plain_text(item.id).unwrap().as_deref(),
        Some("/Users/me/b.pdf\n/Users/me/a folder")
    );
}

#[test]
fn plain_text_of_an_image_is_none_and_of_a_missing_item_an_error() {
    let mut store = Fixture::new().open();
    let item = added(store.record(capture(image(None), 1)).unwrap());
    assert_eq!(store.plain_text(item.id).unwrap(), None);
    assert_eq!(
        store.plain_text(ItemId::from_raw(999)),
        Err(CoreError::ItemNotFound(ItemId::from_raw(999)))
    );
}

// --- Snippets ---

#[test]
fn snippets_are_added_listed_updated_and_deleted() {
    let mut store = Fixture::new().open();
    let sig = store
        .add_snippet("Signature", "Regards,\nAra", at(10))
        .unwrap();
    let addr = store.add_snippet("Address", "1 Main St", at(20)).unwrap();
    assert_eq!(sig.created_at, at(10));
    assert_eq!(sig.updated_at, at(10));
    assert_eq!(store.snippets("").unwrap(), [sig.clone(), addr.clone()]);
    assert_eq!(store.snippet(addr.id).unwrap(), Some(addr.clone()));

    let edited = store
        .update_snippet(sig.id, "Sign-off", "Best,\nAra", at(30))
        .unwrap();
    assert_eq!(edited.title, "Sign-off");
    assert_eq!(edited.created_at, at(10));
    assert_eq!(edited.updated_at, at(30));
    assert_eq!(store.snippet(sig.id).unwrap(), Some(edited));

    store.delete_snippet(sig.id).unwrap();
    assert_eq!(store.snippets("").unwrap(), [addr]);
    assert_eq!(store.snippet(sig.id).unwrap(), None);
}

#[test]
fn invalid_snippets_are_rejected() {
    let mut store = Fixture::new().open();
    let ok = store.add_snippet("a", "b", at(1)).unwrap();
    assert_eq!(
        store.add_snippet("  ", "b", at(1)),
        Err(CoreError::InvalidSnippet(InvalidSnippet::EmptyTitle))
    );
    assert_eq!(
        store.add_snippet("a", "", at(1)),
        Err(CoreError::InvalidSnippet(InvalidSnippet::EmptyBody))
    );
    assert_eq!(
        store.update_snippet(ok.id, "", "b", at(2)),
        Err(CoreError::InvalidSnippet(InvalidSnippet::EmptyTitle))
    );
    assert_eq!(store.snippets("").unwrap(), [ok]);
}

#[test]
fn missing_snippets_are_reported() {
    let mut store = Fixture::new().open();
    let id = SnippetId::from_raw(5);
    assert_eq!(
        store.update_snippet(id, "a", "b", at(1)),
        Err(CoreError::SnippetNotFound(id))
    );
    assert_eq!(
        store.delete_snippet(id),
        Err(CoreError::SnippetNotFound(id))
    );
}

#[test]
fn snippet_search_matches_title_and_body_with_every_word() {
    let mut store = Fixture::new().open();
    let sig = store
        .add_snippet("Signature", "Kind regards", at(1))
        .unwrap();
    let persian = store.add_snippet("آدرس", "خیابان ۱۲ تهران", at(2)).unwrap();
    store.add_snippet("Other", "nothing", at(3)).unwrap();

    assert_eq!(store.snippets("SIGN regards").unwrap(), [sig]);
    assert_eq!(store.snippets("signature missing").unwrap(), []);
    // Persian digits and letter forms fold like in history search.
    assert_eq!(store.snippets("خيابان 12").unwrap(), [persian]);
    // A word that folds to nothing is ignored.
    assert_eq!(store.snippets("\u{200C}").unwrap().len(), 3);
}

#[test]
fn snippets_are_independent_of_history() {
    let mut store = Fixture::new().open();
    store
        .set_limits(HistoryLimits {
            max_items: 1,
            ..HistoryLimits::default()
        })
        .unwrap();
    store
        .set_rules(CaptureRules {
            auto_clear_after: Some(std::time::Duration::from_secs(1)),
            ..CaptureRules::default()
        })
        .unwrap();
    store.add_snippet("keep", "me", at(0)).unwrap();
    store.record(capture(text("a"), 0)).unwrap();
    store.record(capture(text("b"), 1)).unwrap();
    store.clear_expired(at(1_000_000)).unwrap();
    assert_eq!(store.count().unwrap(), 0);
    assert_eq!(store.snippets("").unwrap().len(), 1);
}

#[test]
fn snippets_survive_reopening_and_deleted_ones_leave_no_trace() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let kept = store.add_snippet("kept", "kept body", at(1)).unwrap();
    let gone = store
        .add_snippet("gone", "zebra-unique-body", at(2))
        .unwrap();
    store.delete_snippet(gone.id).unwrap();
    drop(store);

    assert_eq!(fx.open().snippets("").unwrap(), [kept]);
    assert!(!contains(&fx.file_bytes(), "zebra-unique-body"));
}

// --- Export and import ---

fn populated() -> Fixture {
    let fx = Fixture::new();
    let mut store = fx.open();
    let mut safari = capture(rich_text(), 10);
    safari.source_app = Some(app());
    store.record(safari).unwrap();
    store
        .record(capture(image(Some(vec![1, 2, 3])), 20))
        .unwrap();
    store.record(capture(files(), 30)).unwrap();
    let pinned = added(store.record(capture(text("pinned note"), 40)).unwrap());
    store.set_pinned(pinned.id, true).unwrap();
    store.add_snippet("Signature", "Regards", at(50)).unwrap();
    fx
}

/// What an export carries, ignoring ids and the order bookkeeping.
fn essence(store: &Store) -> Vec<String> {
    store
        .list()
        .unwrap()
        .iter()
        .map(|item| {
            format!(
                "{:?}|{}|{:?}|{:?}|{:?}",
                item.content, item.pinned, item.source_app, item.created_at, item.last_copied_at
            )
        })
        .collect()
}

#[test]
fn export_then_import_restores_everything() {
    let source = populated().open();
    let data = source.export().unwrap();

    let mut target = Fixture::new().open();
    let summary = target.import(&data).unwrap();
    assert_eq!(
        summary,
        ImportSummary {
            items_added: 4,
            snippets_added: 1,
            ..ImportSummary::default()
        }
    );
    assert_eq!(essence(&target), essence(&source));
    assert_eq!(target.snippets("").unwrap().len(), 1);
    assert_eq!(target.snippets("").unwrap()[0].title, "Signature");

    // Content, rich text, thumbnails and files all came through intact.
    let items = target.list().unwrap();
    assert_eq!(items[0].content, text("pinned note"));
    assert!(items[0].pinned);
    let ItemContent::Image(img) = &items[2].content else {
        panic!("expected the image");
    };
    assert_eq!(img.thumbnail.as_deref(), Some([1, 2, 3].as_slice()));
}

#[test]
fn importing_twice_adds_nothing() {
    let data = populated().open().export().unwrap();
    let mut target = Fixture::new().open();
    target.import(&data).unwrap();
    let again = target.import(&data).unwrap();
    assert_eq!(
        again,
        ImportSummary {
            items_duplicate: 4,
            snippets_duplicate: 1,
            ..ImportSummary::default()
        }
    );
    assert_eq!(target.count().unwrap(), 4);
    assert_eq!(target.snippets("").unwrap().len(), 1);
}

#[test]
fn imported_items_go_on_top_in_their_original_order() {
    let data = populated().open().export().unwrap();
    let mut target = Fixture::new().open();
    target.record(capture(text("existing"), 5)).unwrap();
    target.import(&data).unwrap();
    let order = texts(&target.list().unwrap());
    assert_eq!(order[0], "pinned note");
    assert_eq!(order.last().map(String::as_str), Some("existing"));
}

#[test]
fn import_respects_size_and_count_limits_and_keeps_pins() {
    let data = populated().open().export().unwrap();

    let mut small = Fixture::new().open();
    small
        .set_limits(HistoryLimits {
            max_item_bytes: 100,
            ..HistoryLimits::default()
        })
        .unwrap();
    let summary = small.import(&data).unwrap();
    // The 4 KiB image and the rich text item are over 100 bytes.
    assert_eq!(summary.items_too_large, 1);
    assert_eq!(summary.items_added, 3);

    let mut tight = Fixture::new().open();
    tight
        .set_limits(HistoryLimits {
            max_items: 1,
            ..HistoryLimits::default()
        })
        .unwrap();
    tight.import(&data).unwrap();
    // One unpinned item (the newest) plus the pinned one.
    assert_eq!(tight.count().unwrap(), 2);
    assert!(tight.list().unwrap().iter().any(|item| item.pinned));
}

#[test]
fn import_ignores_capture_rules() {
    let data = populated().open().export().unwrap();
    let mut target = Fixture::new().open();
    target.set_private_mode(true);
    target
        .set_rules(CaptureRules {
            private_mode: true,
            ignored_apps: vec!["com.apple.Safari".into()],
            ..CaptureRules::default()
        })
        .unwrap();
    assert_eq!(target.import(&data).unwrap().items_added, 4);
}

#[test]
fn an_empty_store_exports_and_imports() {
    let data = Fixture::new().open().export().unwrap();
    let mut target = Fixture::new().open();
    assert_eq!(target.import(&data).unwrap(), ImportSummary::default());
}

#[test]
fn damaged_input_is_rejected_and_changes_nothing() {
    let data = populated().open().export().unwrap();
    let mut target = Fixture::new().open();

    assert!(matches!(
        target.import(b"not an export"),
        Err(CoreError::InvalidExport(_))
    ));
    assert!(matches!(
        target.import(b""),
        Err(CoreError::InvalidExport(_))
    ));
    // Every truncation fails cleanly, including mid-snippet.
    for len in (0..data.len()).step_by(7) {
        assert!(
            matches!(
                target.import(&data[..len]),
                Err(CoreError::InvalidExport(_))
            ),
            "len {len}"
        );
    }
    let mut trailing = data.clone();
    trailing.push(0);
    assert!(matches!(
        target.import(&trailing),
        Err(CoreError::InvalidExport(_))
    ));
    assert_eq!(target.count().unwrap(), 0);
    assert_eq!(target.snippets("").unwrap(), []);
}

#[test]
fn a_huge_claimed_length_does_not_allocate() {
    let mut data = b"CLIPOEXP".to_vec();
    data.extend(1u32.to_le_bytes());
    data.extend(u64::MAX.to_le_bytes());
    let mut target = Fixture::new().open();
    assert!(matches!(
        target.import(&data),
        Err(CoreError::InvalidExport(_))
    ));
}

#[test]
fn a_newer_format_is_refused() {
    let mut data = b"CLIPOEXP".to_vec();
    data.extend(2u32.to_le_bytes());
    let mut target = Fixture::new().open();
    assert_eq!(
        target.import(&data),
        Err(CoreError::UnsupportedExport {
            found: 2,
            supported: 1
        })
    );
}

#[test]
fn invalid_content_inside_an_export_is_rejected_whole() {
    // One text item with empty text.
    let mut data = b"CLIPOEXP".to_vec();
    data.extend(1u32.to_le_bytes());
    data.extend(1u64.to_le_bytes()); // one item
    data.push(0); // not pinned
    data.extend(0i64.to_le_bytes());
    data.extend(0i64.to_le_bytes());
    data.push(0); // no source app
    data.push(0); // text
    data.extend(0u64.to_le_bytes()); // empty plain text
    data.push(0); // no rich text
    data.extend(0u64.to_le_bytes()); // no snippets
    let mut target = Fixture::new().open();
    assert!(matches!(
        target.import(&data),
        Err(CoreError::InvalidContent(_))
    ));
    assert_eq!(target.count().unwrap(), 0);
}
