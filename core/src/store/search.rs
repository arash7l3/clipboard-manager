//! Tab filtering and text search over the history.

use std::fmt;

use rusqlite::functions::FunctionFlags;
use rusqlite::types::Value;
use rusqlite::{params_from_iter, Connection};

use super::codec::{item_columns, kind_code, RawItem};
use super::Store;
use crate::error::CoreError;
use crate::model::redact::Redacted;
use crate::model::{Category, ClipItem};

/// Name of the SQL function that folds text for matching. See [`fold`].
const FOLD_FN: &str = "clipo_fold";

/// Match clause for one search word, with two parameters (text, file path).
const WORD_CLAUSE: &str = " AND (instr(clipo_fold(text_plain), ?) > 0 \
    OR EXISTS (SELECT 1 FROM item_files \
    WHERE item_id = items.id AND instr(clipo_fold(path), ?) > 0))";

/// What the popup asks for: one tab, optionally narrowed by search text.
///
/// `Debug` hides the search text: it is usually part of something copied.
#[derive(Clone, PartialEq, Eq)]
pub struct Query {
    /// The tab to list.
    pub category: Category,
    /// Search text. Split on whitespace; an item must contain every word.
    /// Matching ignores case and, for Persian and Arabic text, the difference
    /// between Arabic and Persian letter forms, digit scripts, diacritics and
    /// zero-width non-joiners. Text items match on their plain text and file
    /// items on their paths; images have no searchable text.
    pub text: String,
    /// Return at most this many items, newest first.
    pub limit: Option<usize>,
}

impl Default for Query {
    fn default() -> Self {
        Self {
            category: Category::All,
            text: String::new(),
            limit: None,
        }
    }
}

impl Query {
    /// Everything listed under `category`.
    #[must_use]
    pub fn category(category: Category) -> Self {
        Self {
            category,
            ..Self::default()
        }
    }

    /// Items matching `text`, across all categories.
    #[must_use]
    pub fn text(text: impl Into<String>) -> Self {
        Self {
            text: text.into(),
            ..Self::default()
        }
    }
}

impl fmt::Debug for Query {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Query")
            .field("category", &self.category)
            .field("text", &Redacted(self.text.len()))
            .field("limit", &self.limit)
            .finish()
    }
}

/// Folds text so that visually or semantically equal strings compare equal:
/// lowercase, Arabic yeh and kaf to their Persian forms, Persian and Arabic-Indic
/// digits to ASCII, and diacritics, tatweel and zero-width non-joiners removed.
pub(super) fn fold(text: &str) -> String {
    text.chars()
        .filter_map(|c| match c {
            '\u{200C}' | '\u{0640}' | '\u{064B}'..='\u{0652}' => None,
            '\u{064A}' | '\u{0649}' => Some('\u{06CC}'),
            '\u{0643}' => Some('\u{06A9}'),
            '\u{0660}'..='\u{0669}' => char::from_digit(u32::from(c) - 0x0660, 10),
            '\u{06F0}'..='\u{06F9}' => char::from_digit(u32::from(c) - 0x06F0, 10),
            _ => Some(c),
        })
        .flat_map(char::to_lowercase)
        .collect()
}

/// Registers [`fold`] on the connection for use in queries.
pub(super) fn register_functions(conn: &Connection) -> Result<(), CoreError> {
    conn.create_scalar_function(
        FOLD_FN,
        1,
        FunctionFlags::SQLITE_UTF8 | FunctionFlags::SQLITE_DETERMINISTIC,
        |ctx| Ok(ctx.get::<Option<String>>(0)?.map(|text| fold(&text))),
    )?;
    Ok(())
}

impl Store {
    /// Items matching `query`, most recently copied first.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure or a corrupt row.
    pub fn search(&self, query: &Query) -> Result<Vec<ClipItem>, CoreError> {
        // Every value is bound, never spliced into the SQL.
        let mut sql = String::from(concat!("SELECT ", item_columns!(), " FROM items WHERE 1"));
        let mut args: Vec<Value> = Vec::new();

        match query.category {
            Category::All => {}
            Category::Pinned => sql.push_str(" AND pinned = 1"),
            Category::Text | Category::Images | Category::Files => {
                sql.push_str(" AND kind = ?");
                args.push(Value::Integer(query.category.kind().map_or(-1, kind_code)));
            }
        }

        // A word that folds to nothing (a lone ZWNJ or diacritic) would match
        // every text and no image, so it is dropped like whitespace.
        let words = query.text.split_whitespace().map(fold);
        for word in words.filter(|word| !word.is_empty()) {
            sql.push_str(WORD_CLAUSE);
            args.push(Value::Text(word.clone()));
            args.push(Value::Text(word));
        }

        sql.push_str(" ORDER BY copy_order DESC");
        if let Some(limit) = query.limit {
            sql.push_str(" LIMIT ?");
            // A limit SQLite cannot hold returns everything anyway.
            args.push(Value::Integer(i64::try_from(limit).unwrap_or(i64::MAX)));
        }

        let raws = self
            .conn
            .prepare(&sql)?
            .query_map(params_from_iter(&args), RawItem::from_row)?
            .collect::<Result<Vec<_>, _>>()?;
        raws.into_iter().map(|raw| self.finish(raw)).collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn debug_hides_search_text() {
        let query = Query::text("hunter2");
        let out = format!("{query:?}");
        assert!(!out.contains("hunter2"), "leaked: {out}");
        assert_eq!(out, "Query { category: All, text: <7 bytes>, limit: None }");
    }

    #[test]
    fn fold_ignores_case() {
        assert_eq!(fold("Hello ÉCOLE"), "hello école");
    }

    #[test]
    fn fold_unifies_persian_and_arabic_forms() {
        // Arabic yeh/kaf and Persian yeh/kaf are the same letters to a Persian reader.
        assert_eq!(fold("كتاب ي"), fold("کتاب ی"));
        assert_eq!(fold("علي"), fold("علی"));
        // Digits in any script compare equal.
        assert_eq!(fold("۱۲۳ ٤٥٦ 789"), "123 456 789");
        // Zero-width non-joiner, tatweel and diacritics do not matter.
        assert_eq!(fold("می\u{200C}خواهم"), fold("میخواهم"));
        assert_eq!(fold("كـتاب"), fold("کتاب"));
        assert_eq!(fold("مَدرَسه"), fold("مدرسه"));
    }
}
