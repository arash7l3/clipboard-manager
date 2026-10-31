//! Snippets: saved text templates that live apart from the clipboard history.

use std::fmt;

use super::redact::Redacted;
use super::time::Timestamp;
use crate::error::InvalidSnippet;

/// Store-assigned identifier of a snippet.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct SnippetId(i64);

impl SnippetId {
    /// Wraps a raw identifier, for example a SQLite row id.
    #[must_use]
    pub const fn from_raw(raw: i64) -> Self {
        Self(raw)
    }

    /// The raw identifier.
    #[must_use]
    pub const fn as_raw(self) -> i64 {
        self.0
    }
}

/// A saved piece of text with a name to find it by.
///
/// Snippets are not history: they are never evicted, never expire, are not
/// subject to private mode or the ignore list, and are not touched by
/// "clear history".
#[derive(Clone, PartialEq, Eq)]
pub struct Snippet {
    /// Store-assigned identifier.
    pub id: SnippetId,
    /// Short name shown in lists and matched by search.
    pub title: String,
    /// The text pasted when the snippet is used.
    pub body: String,
    /// When the snippet was created.
    pub created_at: Timestamp,
    /// When the title or body last changed.
    pub updated_at: Timestamp,
}

/// Checks the rules every stored snippet must meet.
///
/// # Errors
///
/// Returns the first rule the snippet breaks.
pub(crate) fn validate(title: &str, body: &str) -> Result<(), InvalidSnippet> {
    if title.trim().is_empty() {
        return Err(InvalidSnippet::EmptyTitle);
    }
    if body.is_empty() {
        return Err(InvalidSnippet::EmptyBody);
    }
    Ok(())
}

// Snippets are often credentials or addresses, so they are kept out of logs too.
impl fmt::Debug for Snippet {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Snippet")
            .field("id", &self.id)
            .field("title", &Redacted(self.title.len()))
            .field("body", &Redacted(self.body.len()))
            .field("created_at", &self.created_at)
            .field("updated_at", &self.updated_at)
            .finish()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn title_and_body_are_required() {
        assert_eq!(validate("sig", "Regards"), Ok(()));
        assert_eq!(validate("", "x"), Err(InvalidSnippet::EmptyTitle));
        assert_eq!(validate("  \n", "x"), Err(InvalidSnippet::EmptyTitle));
        assert_eq!(validate("sig", ""), Err(InvalidSnippet::EmptyBody));
    }

    #[test]
    fn body_keeps_its_whitespace() {
        assert_eq!(validate("indent", "  \n"), Ok(()));
    }

    #[test]
    fn debug_hides_title_and_body() {
        let at = Timestamp::from_millis(1);
        let snippet = Snippet {
            id: SnippetId::from_raw(1),
            title: "bank pin".into(),
            body: "hunter2".into(),
            created_at: at,
            updated_at: at,
        };
        let out = format!("{snippet:?}");
        assert!(!out.contains("hunter2") && !out.contains("bank"), "{out}");
    }
}
