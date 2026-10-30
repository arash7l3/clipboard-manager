//! Wall-clock timestamps.

use std::time::{SystemTime, UNIX_EPOCH};

/// A point in time as milliseconds since the Unix epoch (UTC).
///
/// Stored as a plain integer so it maps directly to SQLite and to the
/// Swift and C# bindings.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct Timestamp(i64);

impl Timestamp {
    /// Wraps a millisecond count since the Unix epoch.
    #[must_use]
    pub const fn from_millis(millis: i64) -> Self {
        Self(millis)
    }

    /// Milliseconds since the Unix epoch.
    #[must_use]
    pub const fn as_millis(self) -> i64 {
        self.0
    }

    /// The current system time. Saturates instead of overflowing on absurd clocks.
    #[must_use]
    pub fn now() -> Self {
        let millis = match SystemTime::now().duration_since(UNIX_EPOCH) {
            Ok(after) => i64::try_from(after.as_millis()).unwrap_or(i64::MAX),
            Err(before) => i64::try_from(before.duration().as_millis()).map_or(i64::MIN, |m| -m),
        };
        Self(millis)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trips_millis() {
        assert_eq!(Timestamp::from_millis(-5).as_millis(), -5);
        assert_eq!(
            Timestamp::from_millis(1_700_000_000_000).as_millis(),
            1_700_000_000_000
        );
    }

    #[test]
    fn now_is_after_2024() {
        assert!(Timestamp::now() > Timestamp::from_millis(1_704_067_200_000));
    }

    #[test]
    fn orders_chronologically() {
        assert!(Timestamp::from_millis(1) < Timestamp::from_millis(2));
    }
}
