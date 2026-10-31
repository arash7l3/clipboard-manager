//! Snippets: saved text templates, independent of the clipboard history.

use rusqlite::types::Value;
use rusqlite::{params, params_from_iter, Connection, Row};

use super::Store;
use crate::error::CoreError;
use crate::model::{validate_snippet, Snippet, SnippetId, Timestamp};

const COLUMNS: &str = "id, title, body, created_at, updated_at";

/// Match clause for one search word, with two parameters (title, body).
const WORD_CLAUSE: &str =
    " AND (instr(clipo_fold(title), ?) > 0 OR instr(clipo_fold(body), ?) > 0)";

fn from_row(row: &Row<'_>) -> rusqlite::Result<Snippet> {
    Ok(Snippet {
        id: SnippetId::from_raw(row.get(0)?),
        title: row.get(1)?,
        body: row.get(2)?,
        created_at: Timestamp::from_millis(row.get(3)?),
        updated_at: Timestamp::from_millis(row.get(4)?),
    })
}

/// Stores a snippet and returns it. The caller owns the transaction.
pub(super) fn insert_snippet(
    conn: &Connection,
    title: &str,
    body: &str,
    created_at: Timestamp,
    updated_at: Timestamp,
) -> Result<Snippet, CoreError> {
    validate_snippet(title, body)?;
    conn.execute(
        "INSERT INTO snippets (title, body, created_at, updated_at) VALUES (?1, ?2, ?3, ?4)",
        params![title, body, created_at.as_millis(), updated_at.as_millis()],
    )?;
    Ok(Snippet {
        id: SnippetId::from_raw(conn.last_insert_rowid()),
        title: title.to_owned(),
        body: body.to_owned(),
        created_at,
        updated_at,
    })
}

impl Store {
    /// Saves a new snippet, created at `at`. Titles need not be unique.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidSnippet`] if the title is blank or the body empty.
    pub fn add_snippet(
        &mut self,
        title: &str,
        body: &str,
        at: Timestamp,
    ) -> Result<Snippet, CoreError> {
        insert_snippet(&self.conn, title, body, at, at)
    }

    /// Changes a snippet's title and body, recording `at` as the update time.
    /// The old text is overwritten in the database file, as with
    /// [`Store::delete_snippet`].
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidSnippet`] if the new title is blank or the body
    /// empty, [`CoreError::SnippetNotFound`] if no snippet has this id.
    pub fn update_snippet(
        &mut self,
        id: SnippetId,
        title: &str,
        body: &str,
        at: Timestamp,
    ) -> Result<Snippet, CoreError> {
        validate_snippet(title, body)?;
        let changed = self.conn.execute(
            "UPDATE snippets SET title = ?2, body = ?3, updated_at = ?4 WHERE id = ?1",
            params![id.as_raw(), title, body, at.as_millis()],
        )?;
        if changed == 0 {
            return Err(CoreError::SnippetNotFound(id));
        }
        self.purge_deleted()?;
        self.snippet(id)?.ok_or(CoreError::SnippetNotFound(id))
    }

    /// The snippet with this id, if it exists.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn snippet(&self, id: SnippetId) -> Result<Option<Snippet>, CoreError> {
        use rusqlite::OptionalExtension;
        Ok(self
            .conn
            .prepare_cached(&format!("SELECT {COLUMNS} FROM snippets WHERE id = ?1"))?
            .query_row([id.as_raw()], from_row)
            .optional()?)
    }

    /// Snippets whose title or body contains every word of `text`, in the
    /// order they were created. Matching is the same as for history search
    /// (see [`Query::text`](super::Query)); empty `text` lists all snippets.
    ///
    /// # Errors
    ///
    /// [`CoreError::Storage`] on a database failure.
    pub fn snippets(&self, text: &str) -> Result<Vec<Snippet>, CoreError> {
        let mut sql = format!("SELECT {COLUMNS} FROM snippets WHERE 1");
        let mut args: Vec<Value> = Vec::new();
        for word in text.split_whitespace().map(super::search::fold) {
            if word.is_empty() {
                continue;
            }
            sql.push_str(WORD_CLAUSE);
            args.push(Value::Text(word.clone()));
            args.push(Value::Text(word));
        }
        sql.push_str(" ORDER BY id");
        Ok(self
            .conn
            .prepare(&sql)?
            .query_map(params_from_iter(&args), from_row)?
            .collect::<Result<Vec<_>, _>>()?)
    }

    /// Deletes the snippet. Its text is overwritten in the database file.
    ///
    /// # Errors
    ///
    /// [`CoreError::SnippetNotFound`] if no snippet has this id.
    pub fn delete_snippet(&mut self, id: SnippetId) -> Result<(), CoreError> {
        let changed = self
            .conn
            .execute("DELETE FROM snippets WHERE id = ?1", [id.as_raw()])?;
        if changed == 0 {
            return Err(CoreError::SnippetNotFound(id));
        }
        self.purge_deleted()
    }
}
