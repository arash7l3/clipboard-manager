//! The history store object and the key store the apps implement.

use std::path::Path;
use std::sync::{Arc, Mutex, MutexGuard, PoisonError};
use std::time::SystemTime;

use clipo_core::{ItemId, SnippetId, Store, Timestamp};

use crate::error::{ClipoError, KeyStoreError};
use crate::types::{
    CaptureRules, Category, ClipItem, HistoryLimits, ImportSummary, ItemPreview, Query, Recorded,
    Snippet,
};
use crate::Capture;

/// Keeps the database key in the OS credential store. Implemented by each
/// app: the macOS Keychain (a generic password, this-device-only, accessible
/// after first unlock) and the Windows Credential Manager.
///
/// Key bytes cross the FFI boundary in buffers that are freed but not wiped;
/// the apps should not keep their own copies longer than needed.
#[uniffi::export(callback_interface)]
pub trait KeyStore: Send + Sync {
    /// The stored key, or `None` if no key has been saved yet.
    ///
    /// # Errors
    ///
    /// Any failure other than "not found".
    fn load(&self) -> Result<Option<Vec<u8>>, KeyStoreError>;

    /// Saves `key`, replacing any key already stored.
    ///
    /// # Errors
    ///
    /// Any failure writing the credential store.
    fn save(&self, key: Vec<u8>) -> Result<(), KeyStoreError>;
}

/// Lets the core call the app's key store.
struct ForeignKeyStore(Box<dyn KeyStore>);

impl clipo_core::KeyStore for ForeignKeyStore {
    fn load(&self) -> Result<Option<Vec<u8>>, clipo_core::KeyStoreError> {
        Ok(self.0.load()?)
    }

    fn save(&self, key: &[u8]) -> Result<(), clipo_core::KeyStoreError> {
        Ok(self.0.save(key.to_vec())?)
    }
}

/// The encrypted clipboard history.
///
/// Safe to share between threads; calls are serialised. Every call does
/// SQLite I/O, so make them off the UI thread.
#[derive(uniffi::Object)]
pub struct ClipStore {
    store: Mutex<Store>,
}

impl ClipStore {
    /// The store, even if an earlier call panicked: each call leaves the
    /// database consistent, so the lock's state is still valid.
    fn lock(&self) -> MutexGuard<'_, Store> {
        self.store.lock().unwrap_or_else(PoisonError::into_inner)
    }
}

#[uniffi::export]
impl ClipStore {
    /// Opens or creates the database at `path`, with its key kept in
    /// `key_store`. On first run it creates a key and saves it there.
    ///
    /// Limits start at [`default_history_limits`](crate::default_history_limits)
    /// and rules all off; apply the user's settings with
    /// [`set_limits`](Self::set_limits) and [`set_rules`](Self::set_rules).
    ///
    /// # Errors
    ///
    /// [`ClipoError::KeyMissing`] if the file exists but `key_store` has no
    /// key, [`ClipoError::WrongKey`] if the key does not open it,
    /// [`ClipoError::KeyStore`] if `key_store` throws, and others from
    /// [`clipo_core::Store::open_with_key_store`].
    #[uniffi::constructor]
    pub fn open(path: &str, key_store: Box<dyn KeyStore>) -> Result<Arc<Self>, ClipoError> {
        let store = Store::open_with_key_store(Path::new(path), &ForeignKeyStore(key_store))?;
        Ok(Arc::new(Self {
            store: Mutex::new(store),
        }))
    }

    /// Records a clipboard change: skips it if a rule or the size limit says
    /// so, moves an identical item to the top, or adds a new item and evicts
    /// the oldest unpinned ones past the limit.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidContent`] for malformed content, and storage errors.
    pub fn record(&self, capture: Capture) -> Result<Recorded, ClipoError> {
        Ok(self.lock().record(capture.into())?.into())
    }

    /// The item with this id, if it exists.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn get(&self, id: i64) -> Result<Option<ClipItem>, ClipoError> {
        Ok(self.lock().get(ItemId::from_raw(id))?.map(ClipItem::from))
    }

    /// Items in a tab matching the search text, most recently copied first.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn search(&self, query: Query) -> Result<Vec<ClipItem>, ClipoError> {
        let items = self.lock().search(&query.into())?;
        Ok(items.into_iter().map(ClipItem::from).collect())
    }

    /// Previews of the items in a tab matching the search text, in the same
    /// order as [`search`](Self::search). What the popup lists: cheap enough
    /// for every keystroke.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn previews(&self, query: Query) -> Result<Vec<ItemPreview>, ClipoError> {
        let previews = self.lock().previews(&query.into())?;
        Ok(previews.into_iter().map(ItemPreview::from).collect())
    }

    /// Number of stored items.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn count(&self) -> Result<u64, ClipoError> {
        let count = self.lock().count()?;
        Ok(u64::try_from(count).unwrap_or(u64::MAX))
    }

    /// Moves the item to the top, as if it was copied again at `at`.
    ///
    /// # Errors
    ///
    /// [`ClipoError::ItemNotFound`] if no item has this id.
    pub fn touch(&self, id: i64, at: SystemTime) -> Result<(), ClipoError> {
        Ok(self
            .lock()
            .touch(ItemId::from_raw(id), Timestamp::from(at))?)
    }

    /// Pins or unpins the item. Unpinning may evict it, or others, if
    /// history is over the limit.
    ///
    /// # Errors
    ///
    /// [`ClipoError::ItemNotFound`] if no item has this id.
    pub fn set_pinned(&self, id: i64, pinned: bool) -> Result<(), ClipoError> {
        Ok(self.lock().set_pinned(ItemId::from_raw(id), pinned)?)
    }

    /// Gives the item a name to show in place of its content's first line.
    /// The name is trimmed; a blank one takes the name away. The content is
    /// never changed.
    ///
    /// # Errors
    ///
    /// [`ClipoError::ItemNotFound`] if no item has this id.
    pub fn set_title(&self, id: i64, title: &str) -> Result<(), ClipoError> {
        Ok(self.lock().set_title(ItemId::from_raw(id), Some(title))?)
    }

    /// Clears a tab: deletes the unpinned items listed under `category` and
    /// returns how many. Pinned items and snippets are kept, so clearing
    /// [`Category::Pinned`] deletes nothing.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn clear(&self, category: Category) -> Result<u64, ClipoError> {
        let cleared = self.lock().clear(category.into())?;
        Ok(u64::try_from(cleared).unwrap_or(u64::MAX))
    }

    /// Deletes the item. Its content is overwritten in the file.
    ///
    /// # Errors
    ///
    /// [`ClipoError::ItemNotFound`] if no item has this id.
    pub fn delete(&self, id: i64) -> Result<(), ClipoError> {
        Ok(self.lock().delete(ItemId::from_raw(id))?)
    }

    /// The item's content as plain text, for "paste as plain text": the text
    /// without formatting, or the file paths one per line. `None` for an image.
    ///
    /// # Errors
    ///
    /// [`ClipoError::ItemNotFound`] if no item has this id.
    pub fn plain_text(&self, id: i64) -> Result<Option<String>, ClipoError> {
        Ok(self.lock().plain_text(ItemId::from_raw(id))?)
    }

    /// Saves a new snippet, created at `at`.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidSnippet`] if the title is blank or the body empty.
    pub fn add_snippet(
        &self,
        title: &str,
        body: &str,
        at: SystemTime,
    ) -> Result<Snippet, ClipoError> {
        Ok(self
            .lock()
            .add_snippet(title, body, Timestamp::from(at))?
            .into())
    }

    /// Changes a snippet's title and body.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidSnippet`] if the title is blank or the body empty,
    /// [`ClipoError::SnippetNotFound`] if no snippet has this id.
    pub fn update_snippet(
        &self,
        id: i64,
        title: &str,
        body: &str,
        at: SystemTime,
    ) -> Result<Snippet, ClipoError> {
        Ok(self
            .lock()
            .update_snippet(SnippetId::from_raw(id), title, body, Timestamp::from(at))?
            .into())
    }

    /// Snippets whose title or body contains every word of `text` (matched
    /// like history search), in creation order. Empty `text` lists them all.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn snippets(&self, text: &str) -> Result<Vec<Snippet>, ClipoError> {
        let snippets = self.lock().snippets(text)?;
        Ok(snippets.into_iter().map(Snippet::from).collect())
    }

    /// Deletes the snippet. Its text is overwritten in the file.
    ///
    /// # Errors
    ///
    /// [`ClipoError::SnippetNotFound`] if no snippet has this id.
    pub fn delete_snippet(&self, id: i64) -> Result<(), ClipoError> {
        Ok(self.lock().delete_snippet(SnippetId::from_raw(id))?)
    }

    /// The whole history and all snippets as one blob for the app to write to
    /// a file. **Not encrypted**: it holds clipboard content in the clear.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn export_data(&self) -> Result<Vec<u8>, ClipoError> {
        Ok(self.lock().export()?)
    }

    /// Adds the items and snippets of a blob made by
    /// [`export_data`](Self::export_data). Content already stored is skipped;
    /// on error nothing is imported.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidExport`] if the data is not an export or is
    /// damaged, [`ClipoError::UnsupportedExport`] if it is from a newer Clipo,
    /// and storage errors.
    pub fn import_data(&self, data: &[u8]) -> Result<ImportSummary, ClipoError> {
        Ok(self.lock().import(data)?.into())
    }

    /// The current history limits.
    pub fn limits(&self) -> HistoryLimits {
        self.lock().limits().into()
    }

    /// Changes the limits, evicting the oldest unpinned items right away if
    /// history is now over the item limit.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidLimits`] if a limit is zero.
    pub fn set_limits(&self, limits: HistoryLimits) -> Result<(), ClipoError> {
        Ok(self.lock().set_limits(limits.into())?)
    }

    /// The current capture rules.
    pub fn rules(&self) -> CaptureRules {
        self.lock().rules().clone().into()
    }

    /// Replaces the capture rules. Does not clear anything by itself; call
    /// [`clear_expired`](Self::clear_expired) after changing auto-clear.
    ///
    /// # Errors
    ///
    /// [`ClipoError::InvalidRules`] if the auto-clear time is zero.
    pub fn set_rules(&self, rules: CaptureRules) -> Result<(), ClipoError> {
        Ok(self.lock().set_rules(rules.into())?)
    }

    /// Turns private mode on or off, keeping the other rules.
    pub fn set_private_mode(&self, on: bool) {
        self.lock().set_private_mode(on);
    }

    /// Deletes unpinned items last copied longer ago than the auto-clear
    /// time, measured from `now`. Returns how many were deleted; 0 when
    /// auto-clear is off. Call it on a timer and at launch.
    ///
    /// # Errors
    ///
    /// Storage errors.
    pub fn clear_expired(&self, now: SystemTime) -> Result<u64, ClipoError> {
        let cleared = self.lock().clear_expired(Timestamp::from(now))?;
        Ok(u64::try_from(cleared).unwrap_or(u64::MAX))
    }
}
