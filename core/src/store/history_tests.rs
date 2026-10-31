use super::tests::{app, at, files, image, rich_text, text, Fixture};
use super::*;
use crate::model::{
    Capture, Category, FileRef, ImageContent, ImageFormat, ItemKind, PreviewContent, TextContent,
    TextHint, PREVIEW_FILES, PREVIEW_TEXT_CHARS,
};

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
fn copies_after_the_clock_went_back_still_come_first() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(2)).unwrap();
    store.record(capture(text("a"), 100)).unwrap();
    store.record(capture(text("b"), 200)).unwrap();

    // Older than both items: still added on top, and not evicted.
    let c = added(store.record(capture(text("c"), 50)).unwrap());
    assert_eq!(texts(&store.list().unwrap()), ["c", "b"]);
    assert_eq!(store.get(c.id).unwrap(), Some(c));

    // A repeated copy goes to the top whatever its time, and keeps that time.
    let b = bumped(store.record(capture(text("b"), 10)).unwrap());
    assert_eq!(b.last_copied_at, at(10));
    assert_eq!(texts(&store.list().unwrap()), ["b", "c"]);
}

#[test]
fn copies_in_the_same_millisecond_keep_their_order() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("a"), 5)).unwrap();
    store.record(capture(text("b"), 5)).unwrap();
    store.record(capture(text("a"), 5)).unwrap();
    assert_eq!(texts(&store.list().unwrap()), ["a", "b"]);
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
fn eviction_empties_the_wal() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_limits(limits(1)).unwrap();
    store.record(capture(text("a"), 1)).unwrap();
    assert!(fx.wal_len() > 0, "test needs WAL mode");
    store.record(capture(text("b"), 2)).unwrap();
    assert_eq!(fx.wal_len(), 0);
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
fn huge_limits_mean_no_limit() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let huge = HistoryLimits {
        max_items: usize::MAX,
        max_item_bytes: usize::MAX,
    };
    store.set_limits(huge).unwrap();
    assert_eq!(store.limits(), huge);
    store.record(capture(text("a"), 1)).unwrap();
    store.record(capture(text("b"), 2)).unwrap();
    assert_eq!(texts(&store.list().unwrap()), ["b", "a"]);
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
    let huge = Query {
        limit: Some(usize::MAX),
        ..Query::default()
    };
    assert_eq!(store.search(&huge).unwrap().len(), 4);
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
fn words_that_fold_to_nothing_are_ignored() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("hello"), 1)).unwrap();
    store.record(capture(image(None), 2)).unwrap();

    // A lone ZWNJ, tatweel or diacritic is like whitespace: the image stays.
    for blank in ["\u{200C}", "\u{0640}", "\u{064E}"] {
        assert_eq!(found(&store, blank), ["Image", "hello"]);
        assert_eq!(found(&store, &format!("hel {blank}")), ["hello"]);
    }
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

// --- Previews ---

#[test]
fn previews_follow_search_order_and_filters() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let [_, _, _, pinned] = mixed(&mut store);
    store.set_pinned(pinned.id, true).unwrap();
    store.record(capture(text("pinned note"), 5)).unwrap();

    for category in Category::ALL {
        for words in ["", "note", "me", "nothing"] {
            let query = Query {
                category,
                text: words.into(),
                limit: Some(3),
            };
            let previews: Vec<_> = store
                .previews(&query)
                .unwrap()
                .iter()
                .map(|p| p.id)
                .collect();
            assert_eq!(
                previews,
                ids(&store.search(&query).unwrap()),
                "{category:?} {words:?}"
            );
        }
    }
}

#[test]
fn previews_carry_what_a_row_shows() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let note = added(
        store
            .record(Capture {
                source_app: Some(app()),
                ..capture(rich_text(), 1)
            })
            .unwrap(),
    );
    store
        .record(capture(image(Some(vec![1, 2, 3])), 2))
        .unwrap();
    store.record(capture(files(), 3)).unwrap();
    store.set_pinned(note.id, true).unwrap();

    let previews = store.previews(&Query::default()).unwrap();
    let [file_row, image_row, text_row] = previews.as_slice() else {
        panic!("three previews, got {}", previews.len());
    };

    assert_eq!(
        file_row.content,
        PreviewContent::Files {
            files: vec![
                FileRef {
                    path: "/Users/me/b.pdf".into(),
                    size_bytes: Some(1_234_567),
                },
                FileRef {
                    path: "/Users/me/a folder".into(),
                    size_bytes: None,
                },
            ],
            total: 2,
        }
    );
    assert_eq!(
        image_row.content,
        PreviewContent::Image {
            format: ImageFormat::Tiff,
            width: 640,
            height: 480,
            thumbnail: Some(vec![1, 2, 3]),
        }
    );
    assert_eq!(
        text_row.content,
        PreviewContent::Text {
            snippet: "سلام world".into(),
            truncated: false,
            hint: TextHint::Plain,
        }
    );
    assert!(text_row.pinned);
    assert_eq!(text_row.source_app, Some(app()));
    assert_eq!(text_row.last_copied_at, at(1));
    assert_eq!(text_row.content.kind(), ItemKind::Text);
}

#[test]
fn previews_cut_long_text_and_file_lists() {
    let fx = Fixture::new();
    let mut store = fx.open();
    // Multi-byte characters: the cut counts characters, not bytes.
    let long = "س".repeat(PREVIEW_TEXT_CHARS + 1);
    store.record(capture(text(&long), 1)).unwrap();
    let exact = "a".repeat(PREVIEW_TEXT_CHARS);
    store.record(capture(text(&exact), 2)).unwrap();
    let many: Vec<_> = (0..PREVIEW_FILES + 5)
        .map(|n| FileRef {
            path: format!("/tmp/{n}"),
            size_bytes: None,
        })
        .collect();
    store.record(capture(ItemContent::Files(many), 3)).unwrap();

    let previews = store.previews(&Query::default()).unwrap();
    let PreviewContent::Files { files, total } = &previews[0].content else {
        panic!("files first");
    };
    assert_eq!(files.len(), PREVIEW_FILES);
    assert_eq!(files[0].path, "/tmp/0");
    assert_eq!(*total, PREVIEW_FILES + 5);

    let PreviewContent::Text {
        snippet, truncated, ..
    } = &previews[1].content
    else {
        panic!("exact-length text second");
    };
    assert_eq!(snippet, &exact);
    assert!(!truncated);

    let PreviewContent::Text {
        snippet, truncated, ..
    } = &previews[2].content
    else {
        panic!("long text last");
    };
    assert_eq!(snippet.chars().count(), PREVIEW_TEXT_CHARS);
    assert!(long.starts_with(snippet.as_str()));
    assert!(truncated);
}

#[test]
fn previews_hint_links_and_code() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .record(capture(text("https://example.com/path"), 1))
        .unwrap();
    store
        .record(capture(text("if x {\n    y();\n}"), 2))
        .unwrap();
    // A link longer than the preview is cut, so it is not called a link.
    let long_link = format!("https://example.com/{}", "a".repeat(PREVIEW_TEXT_CHARS));
    store.record(capture(text(&long_link), 3)).unwrap();

    let hints: Vec<_> = store
        .previews(&Query::default())
        .unwrap()
        .into_iter()
        .map(|p| match p.content {
            PreviewContent::Text { hint, .. } => hint,
            other => panic!("text expected, got {other:?}"),
        })
        .collect();
    assert_eq!(hints, [TextHint::Plain, TextHint::Code, TextHint::Link]);
}

// --- Clearing a tab ---

/// Text "a" (pinned), text "b", an image, files, and one snippet.
fn tab_mix(store: &mut Store) -> ClipItem {
    let keep = added(store.record(capture(text("a"), 1)).unwrap());
    store.set_pinned(keep.id, true).unwrap();
    store.record(capture(text("b"), 2)).unwrap();
    store.record(capture(image(None), 3)).unwrap();
    store.record(capture(files(), 4)).unwrap();
    store.add_snippet("title", "body", at(5)).unwrap();
    keep
}

#[test]
fn clearing_all_keeps_pinned_items_and_snippets() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let keep = tab_mix(&mut store);
    assert_eq!(store.clear(Category::All).unwrap(), 3);
    assert_eq!(ids(&store.list().unwrap()), [keep.id]);
    assert_eq!(store.snippets("").unwrap().len(), 1);
    assert_eq!(store.clear(Category::All).unwrap(), 0);
}

#[test]
fn clearing_a_kind_tab_deletes_only_that_kind() {
    let fx = Fixture::new();
    let mut store = fx.open();
    tab_mix(&mut store);
    assert_eq!(store.clear(Category::Images).unwrap(), 1);
    assert_eq!(
        listed_kinds(&store),
        [ItemKind::Files, ItemKind::Text, ItemKind::Text]
    );
    assert_eq!(store.clear(Category::Text).unwrap(), 1);
    assert_eq!(listed_kinds(&store), [ItemKind::Files, ItemKind::Text]);
    assert_eq!(store.clear(Category::Files).unwrap(), 1);
    assert_eq!(texts(&store.list().unwrap()), ["a"]);
}

#[test]
fn clearing_the_pinned_tab_deletes_nothing() {
    let fx = Fixture::new();
    let mut store = fx.open();
    tab_mix(&mut store);
    assert_eq!(store.clear(Category::Pinned).unwrap(), 0);
    assert_eq!(store.count().unwrap(), 4);
}

#[test]
fn clearing_overwrites_content_and_file_rows() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(files(), 1)).unwrap();
    store.record(capture(text("cleared"), 2)).unwrap();
    assert!(fx.wal_len() > 0, "test needs WAL mode");
    assert_eq!(store.clear(Category::All).unwrap(), 2);
    assert_eq!(fx.wal_len(), 0);
    let rows: i64 = store
        .conn
        .query_row("SELECT count(*) FROM item_files", [], |row| row.get(0))
        .unwrap();
    assert_eq!(rows, 0);
}

fn listed_kinds(store: &Store) -> Vec<ItemKind> {
    store.list().unwrap().iter().map(ClipItem::kind).collect()
}
