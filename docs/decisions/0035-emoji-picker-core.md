# 0035 — Emoji picker in the core: a built-in list, name search and recent emoji

Status: accepted

**Decision.**
- **A built-in list.** `core/src/emoji/emoji.txt` lists every emoji the picker offers, with its English Unicode name, in picker order. `cargo xtask emoji <emoji-test.txt>` generates it from Unicode's `emoji-test.txt`. It keeps fully-qualified emoji up to Emoji 15.0, without skin-tone variants and without Unicode's "Component" group (skin-tone and hair swatches). That makes 1,869 emoji in 40 KB. The file is built into the library with `include_str!` and parsed once, on first use.
- **Groups as in the macOS picker.** `EmojiGroup` has eight sections in the order of the macOS picker's bottom bar: Smileys & People (Unicode's "Smileys & Emotion" and "People & Body" together), Animals & Nature, Food & Drink, Activities, Travel & Places, Objects, Symbols and Flags. Activities comes before Travel, as on macOS, unlike Unicode's order.
- **Search by name.** `search_emoji(text)` keeps the emoji where every word of `text` starts a word of the name ("smil" finds "smiling face", "mongolia" finds "flag: Mongolia"), ignoring case. A word that is the emoji itself also matches. Results stay in picker order. Empty text lists everything.
- **Recent emoji are stored, not history.** Schema version 5 adds `recent_emoji (emoji, use_order)`. `ClipStore.record_emoji(text)` puts an emoji first and keeps the latest 30 (`recent_emoji_limit()`: five rows of six). `recent_emoji()` lists them, most recent first. `clear_recent_emoji()` forgets them. Limits, auto-clear, private mode and clearing history do not touch them, and they are not in the export.
- **A separate API, not a `Category`.** The core's `Category` stays the history filter. The Emoji tab is added by each app in its own popup (steps 4.8 and 4.9), before Pinned, and is not offered as a start tab.
- The FFI adds `EmojiGroup`, `Emoji` (`text`, `name`, `group`), `EmojiSection` (`group`, `emoji`) and the functions `emoji_sections()`, `search_emoji(text)` and `recent_emoji_limit()`.

**Why.**
- Neither macOS nor Windows gives an app the emoji list with its categories, so the list has to ship with Clipo. Keeping it in the core gives both apps the same emoji, order and search.
- Emoji 15.0 is the newest version that macOS 14 and Windows 11 both draw. A newer emoji would show as a blank box or as separate pieces on the oldest supported systems. Raise `MAX_VERSION` in the generator when the minimum OS moves.
- Skin tones would need a variant chooser in the popup. Without it, the variants would make up half of the list (1,785 of 3,655). They can come later. A recorded variant would then need the list to know it, which `record_emoji` checks.
- A plain text file is easy to review in a diff and to regenerate. The generator checks that every Unicode group is either mapped or left out on purpose, so a new Unicode group fails loudly.
- The macOS picker's "Frequently Used" section shows emoji the user picked, not ones they copied. So recent emoji have their own table. `record_emoji` only accepts emoji in the list, so clipboard text can never reach that table, even by an app mistake.
- Adding a `Category` case would have changed `tab_categories()` and broken both apps' exhaustive switches before their UI existed. History search, clearing a tab and the start-tab setting would also have needed an exception for a tab with no history items.

**Consequences.**
- Names are English only. Farsi names would need a translated list (CLDR annotations), so Farsi users search in English or browse the sections. Search does not know keywords, so "happy" finds nothing and "grinning" does.
- Windows does not draw country flags: Segoe UI Emoji shows two letters instead. The core lists Flags anyway, and the Windows app leaves that section out (step 4.9).
- An emoji pasted from the picker is restored to the clipboard like any item. Like a picked snippet (decision 0029, update), the apps should tell the watcher to skip that write, so it does not land in the history.
- The list adds about 40 KB to the library, and under 0.1 MB of memory once the picker is first opened.
- The Unicode data is under the Unicode License v3; `emoji.txt` keeps its copyright line and a link to the license.
