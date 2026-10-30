//! Wall-clock timestamps.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

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
        Self::from(SystemTime::now())
    }

    /// This instant as a [`SystemTime`], for the bindings. Falls back to the
    /// epoch if the platform's `SystemTime` cannot represent it.
    #[must_use]
    pub fn to_system_time(self) -> SystemTime {
        let offset = Duration::from_millis(self.0.unsigned_abs());
        if self.0 >= 0 {
            UNIX_EPOCH.checked_add(offset)
        } else {
            UNIX_EPOCH.checked_sub(offset)
        }
        .unwrap_or(UNIX_EPOCH)
    }
}

impl From<SystemTime> for Timestamp {
    /// Truncates to whole milliseconds and saturates on absurd values.
    fn from(time: SystemTime) -> Self {
        let millis = match time.duration_since(UNIX_EPOCH) {
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
    fn converts_to_and_from_system_time() {
        for millis in [0, 1, 1_700_000_000_123, -1, -86_400_000] {
            let at = Timestamp::from_millis(millis);
            assert_eq!(Timestamp::from(at.to_system_time()), at);
        }
        assert_eq!(
            Timestamp::from_millis(1_500).to_system_time(),
            UNIX_EPOCH + Duration::from_millis(1_500)
        );
        // Sub-millisecond precision is dropped.
        let time = UNIX_EPOCH + Duration::from_micros(2_999);
        assert_eq!(Timestamp::from(time), Timestamp::from_millis(2));
    }

    #[test]
    fn orders_chronologically() {
        assert!(Timestamp::from_millis(1) < Timestamp::from_millis(2));
    }
}
