# Security and privacy review (step 5.4)

The final review before release: the Rust core, both apps, the packaging scripts and CI, read against the constraints in [BUILD-GUIDE](../BUILD-GUIDE.md) §4. It ran on a Linux machine. The core and the C# tests ran there; Swift and WinUI code compile only in CI. The decisions it led to are in [decision 0026](decisions/0026-security-review.md).

## Threat model

Clipo protects clipboard history from:

- **Someone who gets the files**: a lost or stolen disk without full-disk encryption, another user account on the same machine, a backup, a cloud-synced folder. The history is one SQLCipher database whose key is in the OS credential store (decision 0007).
- **Leaks through Clipo itself**: logs, crash reports, error messages, network calls, temporary files.
- **Secrets copied from password managers**: concealed-clipboard markers and the ignore list.
- **Hostile data**: whatever another app puts on the clipboard, and an export file someone hands the user.
- **The release pipeline**: signing keys and the machines that build releases.

It does not protect from:

- **Malware running as the same user.** It can read the clipboard as things are copied, ask for the key (Windows Credential Manager gives it without a prompt; the macOS login keychain asks the user first) and read the database. Encryption at rest is not meant to stop it.
- **An administrator or root**, or someone with the unlocked session in front of them.
- **The OS's own clipboard features.** Universal Clipboard, Windows clipboard history and cloud clipboard treat an item Clipo restores like any other copy, following the user's OS settings.

## Findings

Severity is for this app and threat model. Everything marked fixed is in the same pull request as this report, with tests where the code can be tested.

| # | Area | Finding | Severity | Status |
|---|---|---|---|---|
| 1 | Core | If `PRAGMA key` failed on a token, rusqlite would quote the statement in the error, and the statement holds the key in hex. The apps log core errors publicly. Not seen to happen (the statement is fixed and valid), but the key would end up in the system log. | Low (unlikely, key exposed if it happens) | Fixed: the unlock error names only the SQLite code. Test: `unlock_errors_never_carry_the_key`. |
| 2 | Core | Editing a snippet left the old text in the WAL until a later checkpoint. Deleting purged it; editing did not. | Low | Fixed: editing purges like deleting. Test: `editing_a_snippet_empties_the_wal`. |
| 3 | macOS | Private mode and ignore-list changes reached the capture check only after a background task gave them to the store. A copy made in that moment, possibly longer if the store was busy, was read and stored. | Low | Fixed: the watcher reads the saved settings, as the Windows app already did. This also takes a store lock off the main thread. |
| 4 | macOS | The pasteboard could change between the concealed check and reading the data, so a concealed copy landing in that instant could be read as the earlier one. | Low | Fixed: the change count is checked again after reading; a moved count leaves the copy to the next poll. Test: `copyLandingWhileReadingIsLeftForTheNextPoll`. |
| 5 | Windows | The worker reads the clipboard after a queue. While it was busy with earlier changes, a newer copy from an ignored app that sets no concealed marker could be read under the previous app's name and stored. | Low | Fixed: with the clipboard open (so its owner cannot change), the app that owns it is checked against the rules too. Compiled in CI; not run on a PC. |
| 6 | macOS | The history folder was in Time Machine backups. A backup keeps items after the user deletes or clears them (secure delete does not reach backups), and the login keychain, with the key, is backed up too. | Low | Fixed: the `Clipo` folder is excluded from backups. Windows needs no change: Windows Backup and File History leave `%LOCALAPPDATA%` out by default. |
| 7 | macOS | An export was written with default permissions and restricted to the user afterwards: readable by other accounts for a moment, and for longer by anyone who opened it in that moment. | Low | Fixed: created owner-only (`O_EXCL`, mode 0600) before anything is written. Test: `exportFileIsTheUsersAloneAndReplacesAnOldOne`. |
| 8 | macOS | Import read a file of any size into memory; the Windows app stops at 512 MB. | Low (robustness) | Fixed: the same 512 MB limit. |
| 9 | CI | Pull requests in this public repository run on the self-hosted Mac. It keeps state between runs (`~/.cargo`, build folders) and is where the Developer ID and notary profile are to live (decision 0023). Code from an approved fork could sign anything with them or leave code in the build folders for a later release build. Hiding the variable names from pull requests does not stop code that uses the keychain directly. | Medium | Reduced: Mac jobs skip pull requests from forks. A fork can edit that line too, so the repository's "require approval for all outside contributors" setting stays the first guard. See recommendation 1. |
| 10 | CI | Signed MSIX builds (tags, manual runs) restored caches written by other runs and ran a third-party action (`Swatinem/rust-cache`) in the job that holds the signing key. The key file stayed on disk after signing. | Low | Fixed: signed builds use no caches, and the key file is deleted after signing. Pull requests still use the caches. |
| 11 | CI | No workflow said what the job token may do. The repository default is already read-only. | Low | Fixed: every workflow sets `contents: read`. |

## Checked, no change needed

| Area | What was checked |
|---|---|
| Encryption at rest | SQLCipher 4 with a random 256-bit raw key. Opening fails unless `cipher_version` answers, so plain SQLite can never store data in the clear. `secure_delete` and a truncating WAL checkpoint after every delete, eviction, auto-clear and clear. Temporary tables stay in memory. The key is wiped after use and never printed. The database file is created owner-only, the macOS folder is 0700 and `%LOCALAPPDATA%` is the user's alone. |
| Key handling | macOS login keychain item (other apps get a keychain prompt); Windows generic credential with `CRED_PERSIST_LOCAL_MACHINE` (does not roam). A missing or wrong key never deletes anything until the user agrees. Key-store errors carry OS status codes only. |
| Logs | Every macOS log line has only an OS status, a fixed text or a core error, and core errors never quote content (tested in the core). Windows `Trace` lines have exception types and messages from code that does not put content in them. No `print`, `NSLog` or `Console` calls. The C# records from UniFFI would print content in `ToString()`, but nothing logs or formats them. |
| Concealed data | macOS: the nspasteboard.org types, 1Password's and Peter Maurer's, checked before any data is read. Windows: `ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore` and `CanIncludeInClipboardHistory` = 0. Built-in ignore lists for common password managers on both. |
| Hostile clipboard data | Sizes are checked before data is copied (Windows checks the global memory size; bitmaps over 128 MiB and images over 100 million pixels are skipped). The DIB and CF_HTML parsers are bounds-checked managed code. Images are decoded by ImageIO and WIC only to make thumbnails. HTML and RTF are stored and written back, never rendered: there is no web view. |
| Hostile export files | The parser checks every length against the bytes left before allocating, validates every item and snippet before writing, and imports in one transaction. Tests cover damaged data and huge claimed lengths. |
| SQL | Every value is bound. Dynamic SQL only joins fixed clauses. |
| Memory safety | `unsafe_code = "deny"` in the core. The C# `unsafe` blocks read clipboard memory within the size Windows reports, and write blocks they allocated themselves. |
| Network | No network code in the core or either app. |
| Auto-paste | Off by default. Sends only Cmd+V or Ctrl+V. On macOS it needs Accessibility, which the app explains once and does without when denied. |
| Login items | `SMAppService` on macOS. The Windows `Run` value quotes the executable path, so a path with spaces cannot run a different program. The MSIX uses a startup task. |
| Packaging | macOS: hardened runtime, no entitlements, not sandboxed (posting key events to other apps needs that; decision 0009). MSIX: only `runFullTrust`. The throwaway test certificate is an end-entity code-signing certificate whose key is destroyed after signing. Pull requests never get signing secrets or names. |
| Windows messages | A spurious `WM_CLIPBOARDUPDATE` changes nothing: updates count only when the clipboard sequence number moves. Clipo does not relax UIPI, so lower-integrity processes cannot send it messages. |

## Known limits, accepted

- **Same-user malware** is out of scope (above). On Windows any process of the user can read the key from Credential Manager.
- **macOS source app.** The pasteboard does not say who wrote to it, so the frontmost app is credited. Switching apps within 0.3 s of copying credits the wrong app (decision 0010). Concealed markers are the real protection for password managers.
- **The export is not encrypted** (decision 0018), and step 5.4 keeps it that way (decision 0026). Both apps warn before writing one.
- **Login keychain** on macOS until there is a Developer ID; then the key moves to the data protection keychain (decision 0023). Until then macOS asks again after every update before giving Clipo its key.
- **Key copies in memory.** SQLCipher keeps the key for the open database, and key bytes cross the FFI in buffers that are freed but not wiped. Reading Clipo's memory needs the same-user access that is out of scope.
- **Network paths.** macOS draws a file row's icon from the first path, and Windows reads every copied file's size. A path on an unreachable or remote volume (an imported `/net/…` path on macOS, a UNC path on Windows) makes the OS contact that server, and the macOS popup can stall while it does. Only a local app or an import the user chose can put such a path there.
- **Huge text on macOS** is read whole on the main thread before the core skips it as too large. A robustness problem, not a leak.
- **Packaged and unpackaged Windows builds share the Credential Manager key** (decision 0025). Starting over in one leaves the other unable to open its history. It asks before deleting anything.

## Recommendations before release

1. **Keep the Developer ID off a machine that runs pull requests.** Before installing the certificate (decision 0023), do one of these. Run pull-request CI on GitHub-hosted macOS runners, which are free for a public repository, and keep the Mac for tag builds only. Or keep the identity in a separate keychain that is unlocked only during a release build. Or sign on another machine or macOS user. Until then, approve outside pull requests only after reading every changed file, workflows and build scripts included.
2. **Pin actions to commit SHAs** once the repository has real secrets, or turn on "require actions to be pinned to a full-length commit SHA" in the repository settings.
3. **Move the key to the data protection keychain** when the Apple account exists, as decision 0023 describes.
4. **A passphrase-protected export** (AEAD with a password-based key derivation) is a candidate for a later step if exports become common.
5. **Check on real machines.** The Windows app has never run on a PC, and the macOS checklist in [macos-verification.md](macos-verification.md) is still open. Security checks to add there: a password copied from a password manager is not stored; nothing is stored while private mode is on, including a copy made right after turning it on; an ignored app's copy is not stored; an export file is readable by the user only; the history folder is excluded from Time Machine (`tmutil isexcluded ~/Library/Application\ Support/Clipo`).
