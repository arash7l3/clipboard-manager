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

**Update (step 4.8, macOS).**
- **The tab.** Emoji sits between Files and Pinned, so the tabs are All, Text, Images, Files, Emoji, Pinned. It is not a start-tab choice, but "Last used tab" reopens it. The popup is 20 pt wider (400 pt) so six tab names fit.
- **The grid.** An `NSCollectionView` with one section per group (headings in the language of the app), 38 pt cells, nine to a row. The grid always reads left to right, also in Farsi, so Left and Right mean what they show; the headings follow the language. A bar of section buttons under the grid jumps to a section; it hides while searching. A "Recent" section (the core's recent emoji) comes first when there are any.
- **Keys.** The search field keeps the keyboard. On this tab Left and Right move through the grid (not the caret), Up, Down, Page Up, Page Down, Home and End move by row, page and to the ends, Enter and Option+Enter pick, and typing searches the emoji by name. Cmd+1 to Cmd+9, Cmd+P and the Clear button do nothing here.
- **Picking.** Like a snippet: the emoji goes on the clipboard (and is pasted with auto-paste), the watcher skips Clipo's own write, and the core's `record_emoji` puts it first among the recent emoji. It does not enter the history.

**Update (step 4.9, Windows).**
- **The tab.** Emoji sits between Files and Pinned, as on macOS. The popup is 40 px wider (460) so six tab names fit, also in Farsi. It is not a start-tab choice, but "Last used tab" reopens it (`AppSettings.StartPopupTab`).
- **The grid.** Nested `ItemsRepeater`s, so only the emoji in view are made: one block per section with a heading, nine 40 px cells to a row, drawn in Segoe UI Emoji. The grid reads left to right in Farsi too. A bar of buttons under it, each showing one emoji of its section, scrolls that section to the top and hides while searching. A "Recent" section comes first when there are any.
- **Without flags.** `EmojiModel` leaves the Flags group out of the sections, the search results and the recent emoji, because Segoe UI Emoji draws a country flag as two letters. The core still lists them.
- **Keys.** Nothing takes the focus: the popup's keys are caught on their way to the app in front, as for the list. On this tab Left and Right (new `PopupKeyAction`s, ignored on the other tabs) and Up, Down, Page Up, Page Down, Home and End move through the grid, Enter picks, and typing searches by name. Ctrl+1 to Ctrl+9 and Ctrl+P do nothing here, and there is no Clear button.
- **Picking.** Like a snippet: `RestoreTextAsync` puts the emoji on the clipboard (and pastes it with auto-paste), the watcher skips Clipo's own write, and `Extras.RecordEmojiAsync` puts it first among the recent emoji. It does not enter the history.
- The model and its moves (`EmojiModel`, `PopupTab`) are plain .NET and tested on any OS; the grid itself is checked by hand on Windows (docs/windows-verification.md).
