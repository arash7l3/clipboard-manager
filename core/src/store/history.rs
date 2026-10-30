//! History policy: duplicate merging, size and count limits, pinning.

use super::Store;
use crate::error::CoreError;
use crate::model::{Capture, ClipItem, ItemId};

/// Default number of unpinned items kept (see `docs/decisions/0004-defaults.md`).
pub const DEFAULT_MAX_ITEMS: usize = 200;

/// Default largest single item, in bytes. Large enough for a Retina screenshot
/// as TIFF, small enough that a stray huge copy cannot bloat the database.
pub const DEFAULT_MAX_ITEM_BYTES: usize = 32 * 1024 * 1024;

/// How much history the store keeps.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct HistoryLimits {
    /// Most unpinned items kept. When a new item pushes the count past this,
    /// the least recently copied unpinned items are deleted. Pinned items do
    /// not count and are never deleted. Must be at least 1.
    pub max_items: usize,
    /// Largest item accepted, measured by [`ItemContent::byte_size`](crate::ItemContent::byte_size).
    /// Bigger captures are skipped, not truncated. Must be at least 1.
    pub max_item_bytes: usize,
}

impl Default for HistoryLimits {
    fn default() -> Self {
        Self {
            max_items: DEFAULT_MAX_ITEMS,
            max_item_bytes: DEFAULT_MAX_ITEM_BYTES,
        }
    }
}

impl HistoryLimits {
    fn validate(self) -> Result<(), CoreError> {
        if self.max_items == 0 || self.max_item_bytes == 0 {
            return Err(CoreError::InvalidLimits);
        }
        Ok(())
    }
}

/// Why a capture was not stored.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum SkipReason {
    /// The content is larger than [`HistoryLimits::max_item_bytes`].
    TooLarge,
}

/// What [`Store::record`] did with a capture.
#[derive(Debug, Clone, PartialEq, Eq)]
#[non_exhaustive]
pub enum Recorded {
    /// New content, stored as a new item.
    Added(ClipItem),
    /// The same content was already in the history. The existing item (pinned
    /// or not, with its original source app and rich form) moved to the top.
    Bumped(ClipItem),
    /// The capture was dropped on purpose; nothing changed.
    Skipped(SkipReason),
}

impl Store {
    /// The current history limits.
    #[must_use]
    pub const fn limits(&self) -> HistoryLimits {
        self.limits
    }

    /// Changes the history limits. Lowering `max_items` deletes the surplus
    /// oldest unpinned items now.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidLimits`] if either limit is zero.
    pub fn set_limits(&mut self, limits: HistoryLimits) -> Result<(), CoreError> {
        limits.validate()?;
        self.limits = limits;
        self.enforce_item_limit()?;
        Ok(())
    }

    /// Records a clipboard change: the entry point for the platform watchers.
    ///
    /// Content already in the history is not stored twice; the existing item
    /// is moved to the top instead. New content is stored, then the oldest
    /// unpinned items beyond [`HistoryLimits::max_items`] are deleted.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidContent`] if the content fails validation, or
    /// [`CoreError::Storage`] on a database failure.
    pub fn record(&mut self, capture: Capture) -> Result<Recorded, CoreError> {
        capture.content.validate()?;
        if capture.content.byte_size() > self.limits.max_item_bytes {
            return Ok(Recorded::Skipped(SkipReason::TooLarge));
        }

        if let Some(id) = self.find_by_hash(&capture.content.content_hash())? {
            self.touch(id, capture.captured_at)?;
            return self
                .get(id)?
                .map(Recorded::Bumped)
                .ok_or(CoreError::ItemNotFound(id));
        }

        let item = self.insert(capture.content, capture.source_app, capture.captured_at)?;
        self.enforce_item_limit()?;
        Ok(Recorded::Added(item))
    }

    /// Pins or unpins the item. Unpinning counts the item against
    /// [`HistoryLimits::max_items`] again, so it may be deleted right away if
    /// it is among the oldest.
    ///
    /// # Errors
    ///
    /// [`CoreError::ItemNotFound`] if no item has this id.
    pub fn set_pinned(&mut self, id: ItemId, pinned: bool) -> Result<(), CoreError> {
        let changed = self.conn.execute(
            "UPDATE items SET pinned = ?2 WHERE id = ?1",
            rusqlite::params![id.as_raw(), pinned],
        )?;
        super::expect_one(changed, id)?;
        if !pinned {
            self.enforce_item_limit()?;
        }
        Ok(())
    }

    /// Deletes the oldest unpinned items beyond the limit. Returns how many.
    fn enforce_item_limit(&mut self) -> Result<usize, CoreError> {
        let keep = super::codec::to_i64(self.limits.max_items, "item limit")?;
        Ok(self.conn.execute(
            "DELETE FROM items WHERE pinned = 0 AND id NOT IN \
             (SELECT id FROM items WHERE pinned = 0 \
              ORDER BY last_copied_at DESC, id DESC LIMIT ?1)",
            [keep],
        )?)
    }
}
