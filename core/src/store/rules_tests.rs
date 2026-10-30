use std::time::Duration;

use super::history_tests::{added, capture, ids, texts};
use super::tests::{at, files, text, Fixture};
use super::*;
use crate::model::{Capture, SourceApp};

const MINUTE: Duration = Duration::from_secs(60);

fn from_app(id: &str, content: ItemContent, millis: i64) -> Capture {
    Capture {
        source_app: Some(SourceApp {
            id: id.into(),
            name: None,
        }),
        ..capture(content, millis)
    }
}

fn ignoring(apps: &[&str]) -> CaptureRules {
    CaptureRules {
        ignored_apps: apps.iter().map(|a| (*a).into()).collect(),
        ..CaptureRules::default()
    }
}

fn after(duration: Duration) -> CaptureRules {
    CaptureRules {
        auto_clear_after: Some(duration),
        ..CaptureRules::default()
    }
}

// --- Defaults ---

#[test]
fn default_rules_store_everything() {
    let fx = Fixture::new();
    let mut store = fx.open();
    assert_eq!(store.rules(), &CaptureRules::default());
    assert!(!store.rules().private_mode);
    assert_eq!(store.rules().ignored_apps, Vec::<String>::new());
    assert_eq!(store.rules().auto_clear_after, None);
    added(store.record(capture(text("a"), 1)).unwrap());
    // With auto-clear off, nothing ever expires.
    assert_eq!(store.clear_expired(at(i64::MAX)).unwrap(), 0);
    assert_eq!(store.count().unwrap(), 1);
}

#[test]
fn rules_do_not_survive_a_reopen() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_private_mode(true);
    drop(store);
    assert_eq!(fx.open().rules(), &CaptureRules::default());
}

// --- Concealed ---

#[test]
fn concealed_captures_are_never_stored() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let secret = Capture {
        concealed: true,
        ..capture(text("hunter2"), 1)
    };
    assert_eq!(
        store.record(secret).unwrap(),
        Recorded::Skipped(SkipReason::Concealed)
    );
    assert_eq!(store.count().unwrap(), 0);
}

#[test]
fn concealed_copy_of_stored_content_does_not_touch_it() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let item = added(store.record(capture(text("same"), 10)).unwrap());
    store.record(capture(text("other"), 20)).unwrap();

    let concealed = Capture {
        concealed: true,
        ..capture(text("same"), 30)
    };
    assert_eq!(
        store.record(concealed).unwrap(),
        Recorded::Skipped(SkipReason::Concealed)
    );
    // Not moved to the top, not re-stamped.
    assert_eq!(texts(&store.list().unwrap()), ["other", "same"]);
    assert_eq!(store.get(item.id).unwrap().unwrap().last_copied_at, at(10));
}

// --- Ignore list ---

#[test]
fn ignored_apps_are_skipped_and_others_are_stored() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .set_rules(ignoring(&[
            "com.1password.1password",
            "com.apple.keychainaccess",
        ]))
        .unwrap();

    assert_eq!(
        store
            .record(from_app("com.1password.1password", text("pw"), 1))
            .unwrap(),
        Recorded::Skipped(SkipReason::IgnoredApp)
    );
    assert_eq!(
        store
            .record(from_app("com.apple.keychainaccess", text("pw2"), 2))
            .unwrap(),
        Recorded::Skipped(SkipReason::IgnoredApp)
    );
    added(
        store
            .record(from_app("com.apple.Safari", text("ok"), 3))
            .unwrap(),
    );
    assert_eq!(texts(&store.list().unwrap()), ["ok"]);
}

#[test]
fn ignore_list_ignores_case_and_needs_the_whole_id() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .set_rules(ignoring(&[r"C:\Program Files\KeePass\KeePass.exe"]))
        .unwrap();

    for (id, ignored) in [
        (r"c:\program files\keepass\keepass.exe", true),
        (r"C:\Program Files\KeePass\KeePass.exe", true),
        (r"C:\Program Files\KeePass\KeePass.exe.bak", false),
        (r"KeePass.exe", false),
        ("", false),
    ] {
        let recorded = store
            .record(from_app(id, text(&format!("copy from {id}")), 1))
            .unwrap();
        assert_eq!(
            matches!(recorded, Recorded::Skipped(SkipReason::IgnoredApp)),
            ignored,
            "{id:?}"
        );
    }
}

#[test]
fn unknown_source_is_not_ignored() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(ignoring(&[""])).unwrap();
    added(store.record(capture(text("a"), 1)).unwrap());
}

#[test]
fn ignored_copy_of_stored_content_does_not_touch_it() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(from_app("good", text("same"), 10)).unwrap();
    store.record(capture(text("other"), 20)).unwrap();
    store.set_rules(ignoring(&["bad"])).unwrap();

    assert_eq!(
        store.record(from_app("bad", text("same"), 30)).unwrap(),
        Recorded::Skipped(SkipReason::IgnoredApp)
    );
    assert_eq!(texts(&store.list().unwrap()), ["other", "same"]);
}

#[test]
fn removing_an_app_from_the_list_stores_it_again() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(ignoring(&["bad"])).unwrap();
    store.record(from_app("bad", text("a"), 1)).unwrap();
    store.set_rules(CaptureRules::default()).unwrap();
    added(store.record(from_app("bad", text("a"), 2)).unwrap());
}

// --- Private mode ---

#[test]
fn private_mode_pauses_capture_and_keeps_history() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("before"), 1)).unwrap();

    store.set_private_mode(true);
    for content in [text("during"), files(), text("before")] {
        assert_eq!(
            store.record(capture(content, 2)).unwrap(),
            Recorded::Skipped(SkipReason::PrivateMode)
        );
    }
    assert_eq!(texts(&store.list().unwrap()), ["before"]);

    store.set_private_mode(false);
    added(store.record(capture(text("after"), 3)).unwrap());
    assert_eq!(texts(&store.list().unwrap()), ["after", "before"]);
}

#[test]
fn private_mode_does_not_examine_the_capture() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_private_mode(true);
    // Even invalid or oversized content is just skipped, never validated or stored.
    assert_eq!(
        store.record(capture(text(""), 1)).unwrap(),
        Recorded::Skipped(SkipReason::PrivateMode)
    );
    store
        .set_limits(HistoryLimits {
            max_item_bytes: 1,
            ..HistoryLimits::default()
        })
        .unwrap();
    assert_eq!(
        store.record(capture(text("big"), 1)).unwrap(),
        Recorded::Skipped(SkipReason::PrivateMode)
    );
}

#[test]
fn set_private_mode_keeps_other_rules() {
    let fx = Fixture::new();
    let mut store = fx.open();
    let rules = CaptureRules {
        ignored_apps: vec!["x".into()],
        auto_clear_after: Some(MINUTE),
        private_mode: false,
    };
    store.set_rules(rules.clone()).unwrap();
    store.set_private_mode(true);
    assert_eq!(
        store.rules(),
        &CaptureRules {
            private_mode: true,
            ..rules
        }
    );
}

#[test]
fn rule_checks_run_privacy_first_then_size() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store
        .set_limits(HistoryLimits {
            max_item_bytes: 1,
            ..HistoryLimits::default()
        })
        .unwrap();
    store.set_rules(ignoring(&["bad"])).unwrap();
    let both = Capture {
        concealed: true,
        ..from_app("bad", text("too big"), 1)
    };
    assert_eq!(
        store.record(both).unwrap(),
        Recorded::Skipped(SkipReason::Concealed)
    );
    assert_eq!(
        store.record(from_app("bad", text("too big"), 1)).unwrap(),
        Recorded::Skipped(SkipReason::IgnoredApp)
    );
    assert_eq!(
        store.record(from_app("fine", text("too big"), 1)).unwrap(),
        Recorded::Skipped(SkipReason::TooLarge)
    );
}

// --- Auto-clear ---

#[test]
fn expired_unpinned_items_are_deleted() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(after(MINUTE)).unwrap();
    store.record(capture(text("old"), 0)).unwrap();
    store.record(capture(text("edge"), 40_000)).unwrap();
    store.record(capture(text("new"), 100_000)).unwrap();

    // At t = 100 s: "old" is 100 s old, "edge" 60 s old (expired exactly), "new" fresh.
    assert_eq!(store.clear_expired(at(100_000)).unwrap(), 2);
    assert_eq!(texts(&store.list().unwrap()), ["new"]);
    assert_eq!(store.clear_expired(at(100_000)).unwrap(), 0);

    assert_eq!(store.clear_expired(at(159_999)).unwrap(), 0);
    assert_eq!(store.clear_expired(at(160_000)).unwrap(), 1);
    assert_eq!(store.count().unwrap(), 0);
}

#[test]
fn expiry_empties_the_wal() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(after(MINUTE)).unwrap();
    store.record(capture(text("old"), 0)).unwrap();
    assert!(fx.wal_len() > 0, "test needs WAL mode");
    assert_eq!(store.clear_expired(at(60_000)).unwrap(), 1);
    assert_eq!(fx.wal_len(), 0);
}

#[test]
fn copying_again_postpones_expiry() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(after(MINUTE)).unwrap();
    store.record(capture(text("a"), 0)).unwrap();
    store.record(capture(text("a"), 50_000)).unwrap();
    assert_eq!(store.clear_expired(at(70_000)).unwrap(), 0);
    assert_eq!(store.clear_expired(at(110_000)).unwrap(), 1);
}

#[test]
fn pinned_items_never_expire() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(after(MINUTE)).unwrap();
    let keep = added(store.record(capture(text("keep"), 0)).unwrap());
    store.record(capture(text("drop"), 0)).unwrap();
    store.set_pinned(keep.id, true).unwrap();

    assert_eq!(store.clear_expired(at(10_000_000)).unwrap(), 1);
    assert_eq!(ids(&store.list().unwrap()), [keep.id]);

    // Unpinned, it becomes subject to expiry like any other item.
    store.set_pinned(keep.id, false).unwrap();
    assert_eq!(store.clear_expired(at(10_000_000)).unwrap(), 1);
}

#[test]
fn expiry_removes_file_rows() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(after(MINUTE)).unwrap();
    store.record(capture(files(), 0)).unwrap();
    store
        .record(capture(text("expiring-secret-0xC0FFEE"), 0))
        .unwrap();
    assert_eq!(store.clear_expired(at(60_000)).unwrap(), 2);

    let rows: i64 = store
        .conn
        .query_row("SELECT count(*) FROM item_files", [], |row| row.get(0))
        .unwrap();
    assert_eq!(rows, 0);
}

#[test]
fn auto_clear_handles_extreme_values() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("a"), 1)).unwrap();
    // A duration beyond i64 milliseconds and clocks at the extremes must not overflow.
    store.set_rules(after(Duration::MAX)).unwrap();
    assert_eq!(store.clear_expired(at(i64::MIN)).unwrap(), 0);
    assert_eq!(store.clear_expired(at(i64::MAX)).unwrap(), 0);
    store.set_rules(after(MINUTE)).unwrap();
    assert_eq!(store.clear_expired(at(i64::MIN)).unwrap(), 0);
    assert_eq!(store.clear_expired(at(i64::MAX)).unwrap(), 1);
}

#[test]
fn zero_auto_clear_is_rejected_and_changes_nothing() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.set_rules(ignoring(&["x"])).unwrap();
    assert_eq!(
        store.set_rules(after(Duration::ZERO)).err(),
        Some(CoreError::InvalidRules)
    );
    assert_eq!(store.rules(), &ignoring(&["x"]));
}

#[test]
fn auto_clear_can_be_turned_off_again() {
    let fx = Fixture::new();
    let mut store = fx.open();
    store.record(capture(text("a"), 0)).unwrap();
    store.set_rules(after(MINUTE)).unwrap();
    store.set_rules(CaptureRules::default()).unwrap();
    assert_eq!(store.clear_expired(at(10_000_000)).unwrap(), 0);
}
