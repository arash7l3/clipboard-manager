//! Recently used emoji.

use super::history_tests::capture;
use super::tests::{text, Fixture};
use super::*;
use crate::emoji;
use crate::model::Category;

fn recent(store: &Store) -> Vec<&'static str> {
    store
        .recent_emoji()
        .unwrap()
        .iter()
        .map(|emoji| emoji.text)
        .collect()
}

const GRIN: &str = "\u{1F600}";
const HEART: &str = "\u{2764}\u{FE0F}";
const FLAG: &str = "\u{1F1F2}\u{1F1F3}";

#[test]
fn recent_emoji_are_listed_most_recent_first() {
    let mut store = Fixture::new().open();
    assert_eq!(recent(&store), Vec::<&str>::new());
    for text in [GRIN, HEART, FLAG] {
        assert!(store.record_emoji(text).unwrap());
    }
    assert_eq!(recent(&store), [FLAG, HEART, GRIN]);
    assert_eq!(store.recent_emoji().unwrap()[1].name, "red heart");
}

#[test]
fn using_an_emoji_again_moves_it_first_without_a_copy() {
    let mut store = Fixture::new().open();
    for text in [GRIN, HEART, FLAG, GRIN] {
        store.record_emoji(text).unwrap();
    }
    assert_eq!(recent(&store), [GRIN, FLAG, HEART]);
}

#[test]
fn only_listed_emoji_are_recorded() {
    let mut store = Fixture::new().open();
    // Clipboard text, an emoji without its variation selector, a skin tone.
    for text in ["hunter2", "", "\u{2764}", "\u{1F44D}\u{1F3FD}"] {
        assert!(!store.record_emoji(text).unwrap(), "{text:?}");
    }
    assert_eq!(recent(&store), Vec::<&str>::new());
}

#[test]
fn only_the_latest_emoji_are_kept() {
    let mut store = Fixture::new().open();
    let used: Vec<&str> = emoji::all()
        .iter()
        .take(RECENT_EMOJI_LIMIT + 5)
        .map(|emoji| emoji.text)
        .collect();
    for text in &used {
        store.record_emoji(text).unwrap();
    }
    let expected: Vec<&str> = used
        .iter()
        .rev()
        .take(RECENT_EMOJI_LIMIT)
        .copied()
        .collect();
    assert_eq!(recent(&store), expected);
}

#[test]
fn recent_emoji_survive_reopening_and_clearing_history() {
    let fixture = Fixture::new();
    let mut store = fixture.open();
    store.record(capture(text("hello"), 1)).unwrap();
    store.record_emoji(HEART).unwrap();
    store.clear(Category::All).unwrap();
    drop(store);

    let store = fixture.open();
    assert_eq!(store.count().unwrap(), 0);
    assert_eq!(recent(&store), [HEART]);
}

#[test]
fn clearing_recent_emoji_leaves_the_history() {
    let mut store = Fixture::new().open();
    store.record(capture(text("hello"), 1)).unwrap();
    store.record_emoji(GRIN).unwrap();
    store.record_emoji(HEART).unwrap();
    assert_eq!(store.clear_recent_emoji().unwrap(), 2);
    assert_eq!(recent(&store), Vec::<&str>::new());
    assert_eq!(store.count().unwrap(), 1);
    assert_eq!(store.clear_recent_emoji().unwrap(), 0);
}

#[test]
fn emoji_no_longer_listed_are_left_out() {
    let mut store = Fixture::new().open();
    store.record_emoji(GRIN).unwrap();
    // As if a later list had dropped it, or an older build had stored it.
    store
        .conn
        .execute(
            "INSERT INTO recent_emoji (emoji, use_order) VALUES ('gone', 99)",
            [],
        )
        .unwrap();
    assert_eq!(recent(&store), [GRIN]);
}
