//! The database key and the interface to the OS credential store that keeps it.
//!
//! The core generates the key; the platform apps only persist it, in the macOS
//! Keychain or the Windows Credential Manager, by implementing [`KeyStore`].

use std::fmt;

use thiserror::Error;
use zeroize::Zeroizing;

use crate::error::CoreError;

/// Length of a [`DatabaseKey`] in bytes (256 bits).
pub const KEY_LEN: usize = 32;

/// Raw 256-bit key for the encrypted history database.
///
/// Not `Clone`, so the key exists in as few places as possible; the bytes are
/// wiped when it is dropped. `Debug` never prints them.
pub struct DatabaseKey(Zeroizing<[u8; KEY_LEN]>);

impl DatabaseKey {
    /// A fresh random key from the OS random number generator.
    ///
    /// # Errors
    ///
    /// [`CoreError::RandomUnavailable`] if the OS generator fails.
    pub fn generate() -> Result<Self, CoreError> {
        let mut bytes = Zeroizing::new([0u8; KEY_LEN]);
        getrandom::fill(bytes.as_mut_slice()).map_err(|_| CoreError::RandomUnavailable)?;
        Ok(Self(bytes))
    }

    /// Wraps key bytes loaded from a key store.
    ///
    /// # Errors
    ///
    /// [`CoreError::InvalidKey`] unless `bytes` is exactly [`KEY_LEN`] long.
    pub fn from_bytes(bytes: &[u8]) -> Result<Self, CoreError> {
        let mut key = Zeroizing::new([0u8; KEY_LEN]);
        if bytes.len() != KEY_LEN {
            return Err(CoreError::InvalidKey);
        }
        key.copy_from_slice(bytes);
        Ok(Self(key))
    }

    /// The raw key bytes, for handing to a [`KeyStore`].
    #[must_use]
    pub fn as_bytes(&self) -> &[u8; KEY_LEN] {
        &self.0
    }

    /// SQL that unlocks a SQLCipher connection with this key. Must run before
    /// anything else on the connection. Uses the raw-key form, so SQLCipher
    /// skips its password KDF, and pins the SQLCipher 4 file format.
    pub(crate) fn unlock_sql(&self) -> Zeroizing<String> {
        const PREFIX: &str = "PRAGMA key = \"x'";
        const SUFFIX: &str = "'\"; PRAGMA cipher_compatibility = 4;";
        const HEX: &[u8; 16] = b"0123456789ABCDEF";

        // Sized up front so the string never reallocates and leaves a copy behind.
        let mut sql = Zeroizing::new(String::with_capacity(
            PREFIX.len() + KEY_LEN * 2 + SUFFIX.len(),
        ));
        sql.push_str(PREFIX);
        for byte in self.0.iter() {
            sql.push(char::from(HEX[usize::from(byte >> 4)]));
            sql.push(char::from(HEX[usize::from(byte & 0x0f)]));
        }
        sql.push_str(SUFFIX);
        sql
    }
}

impl fmt::Debug for DatabaseKey {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("DatabaseKey(<redacted>)")
    }
}

/// Failure reported by a platform [`KeyStore`]. The message must describe the
/// failure (for example an OS status code) and never contain key material.
#[derive(Debug, Clone, PartialEq, Eq, Error)]
#[error("{message}")]
pub struct KeyStoreError {
    /// What went wrong, for diagnostics.
    pub message: String,
}

impl KeyStoreError {
    /// An error with the given diagnostic message.
    #[must_use]
    pub fn new(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
        }
    }
}

/// Persists the database key in the OS credential store.
///
/// Implemented by each platform app: the macOS Keychain (a generic password
/// item, this-device-only, accessible after first unlock) and the Windows
/// Credential Manager.
pub trait KeyStore {
    /// The stored key, or `None` if no key has been saved yet.
    ///
    /// # Errors
    ///
    /// Any failure other than "not found" reading the credential store.
    fn load(&self) -> Result<Option<Vec<u8>>, KeyStoreError>;

    /// Saves `key`, replacing any key already stored.
    ///
    /// # Errors
    ///
    /// Any failure writing the credential store.
    fn save(&self, key: &[u8]) -> Result<(), KeyStoreError>;
}

/// Loads the database key from `store`, wiping the loaded copy afterwards.
/// Returns `None` if the store has no key yet.
pub(crate) fn load_key(store: &dyn KeyStore) -> Result<Option<DatabaseKey>, CoreError> {
    let Some(bytes) = store.load()? else {
        return Ok(None);
    };
    let bytes = Zeroizing::new(bytes);
    DatabaseKey::from_bytes(&bytes).map(Some)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_keys_are_random() {
        let a = DatabaseKey::generate().unwrap();
        let b = DatabaseKey::generate().unwrap();
        assert_ne!(a.as_bytes(), b.as_bytes());
        assert_ne!(a.as_bytes(), &[0u8; KEY_LEN]);
    }

    #[test]
    fn from_bytes_requires_exact_length() {
        assert_eq!(
            DatabaseKey::from_bytes(&[1; KEY_LEN]).unwrap().as_bytes(),
            &[1; KEY_LEN]
        );
        assert_eq!(
            DatabaseKey::from_bytes(&[1; KEY_LEN - 1]).unwrap_err(),
            CoreError::InvalidKey
        );
        assert_eq!(
            DatabaseKey::from_bytes(&[1; KEY_LEN + 1]).unwrap_err(),
            CoreError::InvalidKey
        );
        assert_eq!(
            DatabaseKey::from_bytes(&[]).unwrap_err(),
            CoreError::InvalidKey
        );
    }

    #[test]
    fn unlock_sql_uses_raw_hex_key() {
        let mut bytes = [0u8; KEY_LEN];
        bytes[0] = 0xAB;
        bytes[KEY_LEN - 1] = 0x0F;
        let key = DatabaseKey::from_bytes(&bytes).unwrap();
        let expected = format!(
            "PRAGMA key = \"x'AB{}0F'\"; PRAGMA cipher_compatibility = 4;",
            "00".repeat(KEY_LEN - 2)
        );
        assert_eq!(*key.unlock_sql(), expected);
    }

    #[test]
    fn debug_hides_key() {
        let key = DatabaseKey::from_bytes(&[0x41; KEY_LEN]).unwrap();
        let out = format!("{key:?}");
        assert_eq!(out, "DatabaseKey(<redacted>)");
    }
}
