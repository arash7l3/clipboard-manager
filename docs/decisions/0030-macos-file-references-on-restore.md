# 0030 — macOS keeps copied files as plain paths and never empties the clipboard on restore

Status: accepted. Amends decisions 0010 and 0012.

**Decision.**
- The reader turns every file URL it reads into a plain absolute path (`NSURL.filePathURL`) before it is stored, and drops a URL that has none.
- The writer builds file URLs only from absolute paths. A stored Finder file reference path (`/.file/id=…`), from an item captured before 0.1.6, is resolved back to the file's path, which works while the file exists.
- When none of an item's paths can be written, the writer leaves the clipboard as it was and reports failure, so Clipo beeps and logs the item's id. It also counts a write that left no item on the pasteboard as a failure.

**Why.** On the build Mac (macOS 27), picking a copied file from the history left the clipboard with no items and gave no beep. A check there showed:
- Finder puts file reference URLs (`file:///.file/id=…`) on the pasteboard;
- `NSPasteboard.writeObjects` given a URL made from a reference path, an empty path or a relative one returns `true` but writes nothing, while a plain path, even of a missing or unreadable file, writes one item;
- the old writer cleared the clipboard before writing, so such a path lost what was there and gave nothing back.

A script reading Finder's reference URL got a plain path, so which bad path Clipo stored is not proven. The change covers every path that writes nothing, and the log line names the item if a pick still fails.

**Consequences.**
- An old item holding a reference path whose file has since been deleted cannot be resolved; picking it now beeps and keeps the clipboard as it was.
- Images and text were not affected: they are written as data, and a check showed a PNG lands on the pasteboard with TIFF added by macOS.

**Update (0.1.7).** A later check found that reference URLs were not the cause: Finder received the paste and did nothing. Decision 0031 has the findings and the change.
