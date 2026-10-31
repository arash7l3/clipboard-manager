# 0018 — Extras in the core: snippets table, plain-text view, unencrypted binary export

Status: accepted

**Decision.**
- **Snippets** are a separate table (`snippets`, schema version 3) in the same encrypted database. They have a title and a body, are listed in creation order and searched with the same folding as history (case, Persian and Arabic letter forms, digits). They are not history: limits, auto-clear, private mode, the ignore list and deleting history do not touch them.
- **Paste as plain text** is `ItemContent::plain_text` / `Store::plain_text`: the plain form of a text item, the paths of a file item one per line, nothing for an image. The apps write that to the clipboard instead of the stored formats.
- **Export / import** is `Store::export` and `Store::import`, one binary blob holding the history (oldest first) and all snippets. The format is documented in `core/src/store/transfer.rs`: magic `CLIPOEXP`, a `u32` version, length-prefixed little-endian fields. The apps only move the bytes to and from a file the user picks.
- **Source app and time** were already stored with every item (step 1.1) and are in the export; the UI work is step 4.2 / 4.3. Quick-paste shortcuts need no core change: they use `previews` with a limit of 9.

**Why.**
- A separate table keeps the history rules simple. Snippets in `items` would need an exception in every limit, eviction, expiry and dedup path.
- The binary format needs no new dependency (no JSON or base64 crates), and a bounds-checked reader is short enough to review. Every length is checked against the bytes left before anything is allocated, so a damaged file cannot make the core allocate gigabytes. The whole blob is parsed and every item validated before the first write, and the writes share one transaction: an import either completes or changes nothing.
- A version field lets later releases read old exports. A newer version is refused with a clear error rather than half-read.

**Import rules.**
- Content already stored (same content hash) and snippets with the same title and body are skipped, so importing the same file twice adds nothing.
- Items larger than the current item size limit are skipped and counted.
- Capture rules do not apply: the user chose to import. Pins, source apps and both timestamps are kept.
- Imported items rank as the most recent copies, in their original order, then the history limit is applied. Importing an old export into a full history can therefore push out existing unpinned items. Pinned items are never pushed out.

**Consequences.**
- **The export is not encrypted.** It holds clipboard content, possibly passwords, in the clear. The apps must say so before writing one (steps 4.2, 4.3). A passphrase-protected export needs an AEAD and a key-derivation function; it is left for the final security review (5.4) to decide.
- An export is a plain file the user can lose or sync to a cloud folder. Clipo cannot protect it after it is written.
- Deleting a snippet purges the WAL like deleting an item, so its text does not stay in the file.
