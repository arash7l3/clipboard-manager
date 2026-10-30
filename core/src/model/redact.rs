//! Helpers for `Debug` output that must not reveal clipboard content.

use std::fmt;

/// Formats as `<N bytes>` in place of the sensitive value it stands for.
pub(crate) struct Redacted(pub(crate) usize);

impl fmt::Debug for Redacted {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "<{} bytes>", self.0)
    }
}
