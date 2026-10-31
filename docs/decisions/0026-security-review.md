# 0026 — Security review: history out of backups, forks off the Mac, clean signed builds, export stays unencrypted

Status: accepted. The review itself is [docs/security-review.md](../security-review.md).

**Decision.**

- **The macOS history folder is excluded from backups.** `History.databaseURL` marks `~/Library/Application Support/Clipo` with `isExcludedFromBackup` each time it opens the history.
- **Capture checks read the saved settings.** On macOS the watcher asks `UserDefaults` for private mode and the ignore list, not the store's copy, as the Windows app already did. The core still applies its own copy in `record`.
- **Data is read only for an allowed app.** macOS drops a read if the change count moved while it ran. Windows also checks the app that owns the clipboard while it holds the clipboard open.
- **Fork pull requests never run on the self-hosted Mac.** Each Mac job has `if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository`.
- **Signed Windows builds start clean.** Tag and manual runs of `windows-msix.yml` restore no caches and delete the key file after signing.
- **Every workflow's token is read-only** (`permissions: contents: read`).
- **The export stays unencrypted** (decision 0018 left this to step 5.4). Both apps warn before writing one. macOS creates the file readable by the user only. Windows gives it the folder's permissions, which in the user's folders means the user alone.

**Why.**

- A backup keeps what the user deleted, out of reach of `secure_delete`, and on macOS the key is backed up with the login keychain. A clipboard history is not worth restoring at that price: losing it on a restore is already accepted when the key is lost (decision 0007).
- Settings are where the user's choice lives. The store gets it a moment later, off the main thread, and a copy made in between must not be read.
- The concealed check and the ignore list are only as good as their link to the data actually read.
- The Mac keeps state between runs and is to hold the Developer ID (decision 0023). Hiding the identity's name from pull requests does not stop code that uses the keychain directly. A fork can edit the `if:` line, so the repository's approval setting for outside contributors stays the first guard; the line stops an approved pull request whose harm is in a build script rather than in the workflow.
- Caches are written by other runs and by third-party action code. A signed package should hold only what its own run built.
- An encrypted export needs an AEAD and a password-based key derivation in the core, plus passphrase screens and their translations in both apps. That is a feature, not a review fix, and the warning already tells the user what the file is.

**Consequences.**

- Restoring a Mac from Time Machine brings back the key but not the history. Clipo starts an empty history with that key.
- Pull requests from forks get no macOS CI. A maintainer can push the branch to the repository to test it.
- Signed Windows builds take longer, because the vendored OpenSSL is compiled from scratch.
- An export is still readable by anyone who gets the file. A passphrase-protected format would be a new export version.
