use super::tests::{app, at, files, image, rich_text, text, Fixture};
use super::*;
use crate::model::{Capture, Category, FileRef, ImageContent, ImageFormat, TextContent};

pub(super) fn capture(content: ItemContent, millis: i64) -> Capture {
    Capture {
        content,
        source_app: None,
        captured_at: at(millis),
        concealed: false,
    }
}

pub(super) fn added(recorded: Recorded) -> ClipItem {
    match recorded {
        Recorded::Added(item) => item,
        other => panic!("expected Added, got {other:?}"),
    }
}

fn bumped(recorded: Recorded) -> ClipItem {
    match recorded {
        Recorded::Bumped(item) => item,
        other => panic!("expected Bumped, got {other:?}"),
    }
}

pub(super) fn texts(items: &[ClipItem]) -> Vec<String> {
    items
        .iter()
        .map(|item| match &item.content {
            ItemContent::Text(t) => t.plain.clone(),
            other => format!("{:?}", other.kind()),
        })
        .collect()
}

fn limits(max_items: usize) -> HistoryLimits {
    HistoryLimits {
        max_items,
        ..HistoryLimits::default()
    }
}

/// A distinct image per `n`, so images never dedup against each other.
fn picture(n: u8, bytes: usize) -> ItemContent {
    ItemContent::Image(ImageContent {
        format: ImageFormat::Png,
        width: 1,
        height: 1,
        data: vec![n; bytes],
        thumbnail: None,
    })
}

// --- Dedup ---

#[test]
fn defaults_follow_the_decision_record() {
    let fx = Fixture::new();
    let store = fx.open();
    assert_eq!(store.limits().max_items, 200);
    assert_eq!(store.limits().max_item_bytes, 32 * 1024 * 1024);
}

#[test]
fn new_content_is_added() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = added(store.record(capture(text("a"), 1)).unwrap());
    assert_eq!(store.get(item.id).unwrap(), Some(item));
    assert_eq!(store.count().unwrap(), 1);
}

#[test]
fn repeated_copy_moves_the_item_to_the_top() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let a = added(store.record(capture(text("a"), 10)).unwrap());
    added(store.record(capture(text("b"), 20)).unwrap());

    let again = bumped(store.record(capture(text("a"), 30)).unwrap());
    assert_eq!(again.id, a.id);
    assert_eq!(again.created_at, at(10));
    assert_eq!(again.last_copied_at, at(30));
    assert_eq!(texts(&store.list().unwrap()), ["a", "b"]);
    assert_eq!(store.count().unwrap(), 2);
}

#[test]
fn duplicate_keeps_original_source_pin_and_rich_form() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let mut first = capture(rich_text(), 10);
    first.source_app = Some(app());
    let item = added(store.record(first).unwrap());
    store.set_pinned(item.id, true).unwrap();

    // Same plain text, no rich form, another app.
    let ItemContent::Text(original) = rich_text() else {
        unreachable!()
    };
    let mut second = capture(
        ItemContent::Text(TextContent {
            plain: original.plain,
            rich: None,
        }),
        20,
    );
    second.source_app = Some(crate::SourceApp {
        id: "other".into(),
        name: None,
    });
    let again = bumped(store.record(second).unwrap());

    assert_eq!(again.id, item.id);
    assert!(again.pinned);
    assert_eq!(again.source_app, Some(app()));
    assert_eq!(again.content, rich_text());
}

#[test]
fn dedup_applies_to_images_and_files() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let image = added(store.record(capture(image(None), 1)).unwrap());
    let files = added(store.record(capture(files(), 2)).unwrap());

    // A different thumbnail or file size is still the same content.
    assert_eq!(
        bumped(
            store
                .record(capture(super::tests::image(Some(vec![1])), 3))
                .unwrap()
        )
        .id,
        image.id
    );
    let ItemContent::Files(mut list) = super::tests::files() else {
        unreachable!()
    };
    list[0].size_bytes = Some(1);
    assert_eq!(
        bumped(store.record(capture(ItemContent::Files(list), 4)).unwrap()).id,
        files.id
    );
    assert_eq!(store.count().unwrap(), 2);
}

#[test]
fn invalid_content_is_rejected() {
    let fx = Fixture::new();
    let mut store = fx.open();
    assert!(matches!(
        store.record(capture(text(""), 1)),
        Err(CoreError::InvalidContent(_))
    ));
    assert_eq!(store.count().unwrap(), 0);
}

// --- Limits ---

#[test]
fn oldest_unpinned_items_are_evicted_past_the_limit() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(3)).unwrap();
    for (i, word) in ["a", "b", "c", "d", "e"].into_iter().enumerate() {
        store
            .record(capture(text(word), i64::try_from(i).unwrap() + 1))
            .unwrap();
    }
    assert_eq!(texts(&store.list().unwrap()), ["e", "d", "c"]);
}

#[test]
fn copying_again_protects_an_item_from_eviction() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(2)).unwrap();
    store.record(capture(text("a"), 1)).unwrap();
    store.record(capture(text("b"), 2)).unwrap();
    store.record(capture(text("a"), 3)).unwrap();
    store.record(capture(text("c"), 4)).unwrap();
    assert_eq!(texts(&store.list().unwrap()), ["c", "a"]);
}

#[test]
fn pinned_items_survive_and_do_not_count() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(2)).unwrap();
    let oldest = added(store.record(capture(text("pinned"), 1)).unwrap());
    store.set_pinned(oldest.id, true).unwrap();
    for (i, word) in ["a", "b", "c", "d"].into_iter().enumerate() {
        store
            .record(capture(text(word), i64::try_from(i).unwrap() + 2))
            .unwrap();
    }
    // Two unpinned items plus the pinned one.
    assert_eq!(texts(&store.list().unwrap()), ["d", "c", "pinned"]);
}

#[test]
fn unpinning_applies_the_limit_again() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(2)).unwrap();
    let old = added(store.record(capture(text("old"), 1)).unwrap());
    store.set_pinned(old.id, true).unwrap();
    store.record(capture(text("b"), 2)).unwrap();
    store.record(capture(text("c"), 3)).unwrap();
    assert_eq!(store.count().unwrap(), 3);

    store.set_pinned(old.id, false).unwrap();
    assert_eq!(texts(&store.list().unwrap()), ["c", "b"]);
    assert_eq!(store.get(old.id).unwrap(), None);
}

#[test]
fn lowering_the_limit_deletes_surplus_now() {
    let fx = Fixture::new();
    let mut store = fx.open();
    for (i, word) in ["a", "b", "c", "d"].into_iter().enumerate() {
        store
            .record(capture(text(word), i64::try_from(i).unwrap() + 1))
            .unwrap();
    }
    store.set_limits(limits(2)).unwrap();
    assert_eq!(texts(&store.list().unwrap()), ["d", "c"]);
}

#[test]
fn evicted_files_leave_no_file_rows() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(1)).unwrap();
    store.record(capture(files(), 1)).unwrap();
    store.record(capture(text("x"), 2)).unwrap();
    let rows: i64 = store
        .conn
        .query_row("SELECT count(*) FROM item_files", [], |row| row.get(0))
        .unwrap();
    assert_eq!(rows, 0);
}

#[test]
fn zero_limits_are_rejected_and_change_nothing() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let before = store.limits();
    assert_eq!(
        store.set_limits(limits(0)).err(),
        Some(CoreError::InvalidLimits)
    );
    assert_eq!(
        store
            .set_limits(HistoryLimits {
                max_item_bytes: 0,
                ..before
            })
            .err(),
        Some(CoreError::InvalidLimits)
    );
    assert_eq!(store.limits(), before);
}

#[test]
fn oversized_content_is_skipped() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .set_limits(HistoryLimits {
            max_item_bytes: 100,
            ..HistoryLimits::default()
        })
        .unwrap();

    assert_eq!(
        store.record(capture(picture(1, 101), 1)).unwrap(),
        Recorded::Skipped(SkipReason::TooLarge)
    );
    assert_eq!(
        store.record(capture(text(&"x".repeat(101)), 2)).unwrap(),
        Recorded::Skipped(SkipReason::TooLarge)
    );
    assert_eq!(store.count().unwrap(), 0);

    // Exactly at the limit is accepted.
    added(store.record(capture(picture(1, 100), 3)).unwrap());
}

#[test]
fn limits_do_not_survive_a_reopen_but_history_does() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(1)).unwrap();
    store.record(capture(text("a"), 1)).unwrap();
    drop(store);
    let store = fx.open();
    assert_eq!(store.limits(), HistoryLimits::default());
    assert_eq!(store.count().unwrap(), 1);
}

// --- Categories ---

fn mixed(store: &mut Store) -> [ClipItem; 4] {
    [
        added(store.record(capture(text("note"), 1)).unwrap()),
        added(store.record(capture(image(None), 2)).unwrap()),
        added(store.record(capture(files(), 3)).unwrap()),
        added(store.record(capture(text("pinned note"), 4)).unwrap()),
    ]
}

pub(super) fn ids(items: &[ClipItem]) -> Vec<ItemId> {
    items.iter().map(|i| i.id).collect()
}

#[test]
fn each_tab_lists_its_items() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let [note, img, file, pinned] = mixed(&mut store);
    store.set_pinned(pinned.id, true).unwrap();
    let tab = |c| ids(&store.search(&Query::category(c)).unwrap());

    assert_eq!(tab(Category::All), [pinned.id, file.id, img.id, note.id]);
    assert_eq!(tab(Category::Text), [pinned.id, note.id]);
    assert_eq!(tab(Category::Images), [img.id]);
    assert_eq!(tab(Category::Files), [file.id]);
    assert_eq!(tab(Category::Pinned), [pinned.id]);
}

#[test]
fn search_result_matches_category_includes() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let [_, _, _, pinned] = mixed(&mut store);
    store.set_pinned(pinned.id, true).unwrap();
    for category in Category::ALL {
        let expected: Vec<_> = store
            .list()
            .unwrap()
            .into_iter()
            .filter(|i| category.includes(i))
            .map(|i| i.id)
            .collect();
        assert_eq!(
            ids(&store.search(&Query::category(category)).unwrap()),
            expected,
            "{category:?}"
        );
    }
}

#[test]
fn limit_keeps_the_newest() {
    let fx = Fixture::new();
    let mut store = fx.open();
    mixed(&mut store);
    let query = Query {
        limit: Some(2),
        ..Query::default()
    };
    assert_eq!(store.search(&query).unwrap().len(), 2);
    let none = Query {
        limit: Some(0),
        ..Query::default()
    };
    assert_eq!(ids(&store.search(&none).unwrap()), []);
    assert_eq!(store.search(&Query::default()).unwrap().len(), 4);
}

// --- Search ---

fn found(store: &Store, query: &str) -> Vec<String> {
    texts(&store.search(&Query::text(query)).unwrap())
}

#[test]
fn search_ignores_case_and_finds_substrings() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("Hello World"), 1)).unwrap();
    store.record(capture(text("goodbye"), 2)).unwrap();

    assert_eq!(found(&store, "hello"), ["Hello World"]);
    // Words match anywhere, so "LLO W" is the words "llo" and "w".
    assert_eq!(found(&store, "LLO W"), ["Hello World"]);
    assert_eq!(found(&store, "orl"), ["Hello World"]);
    assert_eq!(found(&store, "nothing"), Vec::<String>::new());
    // Empty or blank text lists everything.
    assert_eq!(found(&store, ""), ["goodbye", "Hello World"]);
    assert_eq!(found(&store, "  \t "), ["goodbye", "Hello World"]);
}

#[test]
fn every_word_must_match_in_any_order() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("red apple pie"), 1)).unwrap();
    store.record(capture(text("green apple"), 2)).unwrap();

    assert_eq!(found(&store, "apple"), ["green apple", "red apple pie"]);
    assert_eq!(found(&store, "pie  red"), ["red apple pie"]);
    assert_eq!(found(&store, "apple blue"), Vec::<String>::new());
}

#[test]
fn search_is_unicode_aware() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .record(capture(text("École Polytechnique"), 1))
        .unwrap();
    store
        .record(capture(text("می\u{200C}خواهم کتاب ۱۲۳"), 2))
        .unwrap();

    assert_eq!(found(&store, "école"), ["École Polytechnique"]);
    // Arabic kaf and yeh, ASCII digits and a missing ZWNJ all still match.
    assert_eq!(found(&store, "كتاب"), ["می\u{200C}خواهم کتاب ۱۲۳"]);
    assert_eq!(found(&store, "میخواهم"), ["می\u{200C}خواهم کتاب ۱۲۳"]);
    assert_eq!(found(&store, "123"), ["می\u{200C}خواهم کتاب ۱۲۳"]);
}

#[test]
fn search_matches_file_paths_and_not_images() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let file = added(store.record(capture(files(), 1)).unwrap());
    store.record(capture(image(None), 2)).unwrap();

    assert_eq!(
        ids(&store.search(&Query::text("B.PDF")).unwrap()),
        [file.id]
    );
    assert_eq!(
        ids(&store.search(&Query::text("a folder")).unwrap()),
        [file.id]
    );
    // A file list is one item however many of its paths match.
    assert_eq!(store.search(&Query::text("/Users/me")).unwrap().len(), 1);
    assert_eq!(ids(&store.search(&Query::text("tiff")).unwrap()), []);
}

#[test]
fn search_combines_with_category() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("report draft"), 1)).unwrap();
    store
        .record(capture(
            ItemContent::Files(vec![FileRef {
                path: "/tmp/report.pdf".into(),
                size_bytes: None,
            }]),
            2,
        ))
        .unwrap();

    let query = |category| Query {
        category,
        text: "report".into(),
        limit: None,
    };
    assert_eq!(store.search(&query(Category::All)).unwrap().len(), 2);
    assert_eq!(store.search(&query(Category::Text)).unwrap().len(), 1);
    assert_eq!(store.search(&query(Category::Files)).unwrap().len(), 1);
    assert_eq!(ids(&store.search(&query(Category::Images)).unwrap()), []);
    assert_eq!(ids(&store.search(&query(Category::Pinned)).unwrap()), []);
}

#[test]
fn search_text_is_never_run_as_sql() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("100% sure"), 1)).unwrap();
    store.record(capture(text("a_b"), 2)).unwrap();

    for hostile in ["'; DROP TABLE items; --", "\" OR 1=1 --", "%", "_"] {
        let hits = found(&store, hostile);
        // `%` and `_` are plain characters, not wildcards.
        assert_eq!(hits.len(), usize::from(hostile == "%" || hostile == "_"));
    }
    assert_eq!(found(&store, "%"), ["100% sure"]);
    assert_eq!(found(&store, "_"), ["a_b"]);
    assert_eq!(store.count().unwrap(), 2);
}
