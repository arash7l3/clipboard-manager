//! Recently used emoji, for the picker's first section. Kept apart from the
//! history: limits, auto-clear, private mode and clearing history never touch
//! them.

use rusqlite::params;

use super::Store;
use crate::emoji::{self, Emoji};
use crate::error::CoreError;

/// How many recently used emoji are kept: five rows of six in the picker.
pub const RECENT_EMOJI_LIMIT: usize = 30;

impl Store {
    /// Puts `text` first among the recently used emoji, dropping the oldest
    /// past [`RECENT_EMOJI_LIMIT`]. Only emoji the picker offers are recorded
    /// (see [`emoji::find`]), so clipboard text never ends up here; for any
    /// other text it does nothing and returns `false`.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn record_emoji(&mut self, text: &str) -> Result<bool, CoreError> {
        if emoji::find(text).is_none() {
            return Ok(false);
        }
        let limit = i64::try_from(RECENT_EMOJI_LIMIT).unwrap_or(i64::MAX);
        let tx = self.conn.transaction()?;
        tx.execute(
            "INSERT INTO recent_emoji (emoji, use_order) \
             VALUES (?1, (SELECT coalesce(max(use_order), 0) + 1 FROM recent_emoji)) \
             ON CONFLICT (emoji) DO UPDATE SET use_order = excluded.use_order",
            [text],
        )?;
        tx.execute(
            "DELETE FROM recent_emoji WHERE emoji NOT IN \
             (SELECT emoji FROM recent_emoji ORDER BY use_order DESC LIMIT ?1)",
            params![limit],
        )?;
        tx.commit()?;
        Ok(true)
    }

    /// The recently used emoji, most recent first. Any the picker no longer
    /// offers are left out.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn recent_emoji(&self) -> Result<Vec<Emoji>, CoreError> {
        let texts = self
            .conn
            .prepare_cached("SELECT emoji FROM recent_emoji ORDER BY use_order DESC")?
            .query_map([], |row| row.get::<_, String>(0))?
            .collect::<Result<Vec<_>, _>>()?;
        Ok(texts.iter().filter_map(|text| emoji::find(text)).collect())
    }

    /// Forgets every recently used emoji and returns how many there were.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn clear_recent_emoji(&mut self) -> Result<usize, CoreError> {
        let deleted = self.conn.execute("DELETE FROM recent_emoji", [])?;
        self.purge_deleted()?;
        Ok(deleted)
    }
}
