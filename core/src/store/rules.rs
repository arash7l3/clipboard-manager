//! Capture rules: private mode, the app ignore list, concealed items and auto-clear.

use std::time::Duration;

use super::history::SkipReason;
use super::Store;
use crate::error::CoreError;
use crate::model::{Capture, Timestamp};

/// Which captures the store refuses, and when history expires.
///
/// Like [`HistoryLimits`](super::HistoryLimits), the rules live in memory and
/// start at their defaults on every open; the apps keep the user's settings
/// and call [`Store::set_rules`] at launch.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct CaptureRules {
    /// While on, every capture is dropped without being looked at. Existing
    /// history is untouched.
    pub private_mode: bool,
    /// Apps whose copies are never stored, by [`SourceApp::id`](crate::SourceApp::id):
    /// bundle identifiers on macOS, executable paths or `AppUserModelID`s on
    /// Windows. Compared ignoring case. Captures with no known source app are
    /// not matched by any entry.
    pub ignored_apps: Vec<String>,
    /// Unpinned items not copied for this long are deleted by
    /// [`Store::clear_expired`]. `None` keeps items until the history limit
    /// evicts them. Must not be zero.
    pub auto_clear_after: Option<Duration>,
}

impl CaptureRules {
    /// Whether `capture` must not be stored, and why. Checked in the order of
    /// how much the reason says about the user's intent: private mode first, so
    /// nothing about a capture is examined while it is on.
    pub(super) fn skip_reason(&self, capture: &Capture) -> Option<SkipReason> {
        if self.private_mode {
            return Some(SkipReason::PrivateMode);
        }
        if capture.concealed {
            return Some(SkipReason::Concealed);
        }
        let app = capture.source_app.as_ref()?;
        self.ignored_apps
            .iter()
            .any(|ignored| ignored.to_lowercase() == app.id.to_lowercase())
            .then_some(SkipReason::IgnoredApp)
    }

    fn validate(&self) -> Result<(), CoreError> {
        if self.auto_clear_after == Some(Duration::ZERO) {
            return Err(CoreError::InvalidRules);
        }
        Ok(())
    }
}

impl Store {
    /// The current capture rules.
    #[must_use]
    pub const fn rules(&self) -> &CaptureRules {
        &self.rules
    }

    /// Replaces the capture rules. Takes effect for the next
    /// [`Store::record`]; call [`Store::clear_expired`] to apply a new
    /// auto-clear time to existing items.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidRules`] if `auto_clear_after` is zero.
    pub fn set_rules(&mut self, rules: CaptureRules) -> Result<(), CoreError> {
        rules.validate()?;
        self.rules = rules;
        Ok(())
    }

    /// Turns private mode on or off, leaving the other rules as they are.
    pub fn set_private_mode(&mut self, on: bool) {
        self.rules.private_mode = on;
    }

    /// Deletes unpinned items last copied `auto_clear_after` or longer before
    /// `now`. Returns how many were deleted; zero when auto-clear is off.
    ///
    /// The core has no timer: the apps call this at launch and on a timer.
    /// Deleted pages are overwritten, so expired content does not linger in the file.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn clear_expired(&mut self, now: Timestamp) -> Result<usize, CoreError> {
        let Some(after) = self.rules.auto_clear_after else {
            return Ok(0);
        };
        let age_millis = i64::try_from(after.as_millis()).unwrap_or(i64::MAX);
        let cutoff = now.as_millis().saturating_sub(age_millis);
        Ok(self.conn.execute(
            "DELETE FROM items WHERE pinned = 0 AND last_copied_at <= ?1",
            [cutoff],
        )?)
    }
}
