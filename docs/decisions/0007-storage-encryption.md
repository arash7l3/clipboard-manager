# 0007 — Storage encryption: SQLCipher with a keychain-held raw key

Status: accepted

**Decision.** The history is one SQLCipher database (SQLCipher 4 format, AES-256 with per-page HMAC). The core generates a random 256-bit key and opens the database with it as a raw key. The platform apps store that key in the OS credential store (macOS Keychain, Windows Credential Manager) through the core's `KeyStore` interface.

**Why.**
- Whole-file encryption covers metadata too: source apps, timestamps, item kinds and the schema. Encrypting only content columns would leave a readable record of which apps were used and when.
- SQL search, ordering and constraints keep working, because decryption happens below SQLite.
- A random raw key needs no password KDF, so opening is fast. The key never leaves the credential store except in memory, where it is wiped after use.
- Builds stay simple. Apple targets use CommonCrypto (no OpenSSL). Other targets build a vendored OpenSSL, so nothing has to be installed on the machine.

**Safeguards.**
- Opening fails unless SQLCipher answers `PRAGMA cipher_version`, so a build with plain SQLite cannot silently store data in the clear.
- `secure_delete` overwrites freed pages, and temporary data stays in memory.
- On Unix the database file is created readable by its owner only.
- The core never replaces a key on its own. If the key is missing or wrong, opening fails and the app asks the user before deleting the history.

**Consequences.**
- Losing the key (keychain reset, new user profile) loses the history. That is accepted for a clipboard history.
- Windows and Linux builds compile OpenSSL from source. The first build takes several minutes and needs Perl. Later builds are cached.
- The `rusqlite` version sets the minimum Rust version (1.89).
