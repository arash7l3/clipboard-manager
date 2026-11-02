# Windows checks still to try on a PC

The unit tests cover the models, keys and strings; these need the real popup.

## Emoji tab (step 4.9, 0.1.12)

- [ ] The tabs read All, Text, Images, Files, Emoji, Pinned, all names fully visible, also with the Farsi language; Tab and Shift+Tab pass through Emoji; the trash button is hidden on Emoji.
- [ ] The grid shows headings (Smileys & People first, Symbols last, no Flags), nine emoji to a row, and the bar under it has one button per section; clicking a button scrolls that heading to the top and tints the button.
- [ ] Arrow keys move a highlight through the grid with text in the search or not; Up and Down keep their column across sections; Page Up, Page Down, Home and End work; the highlight scrolls into view; Narrator says the emoji's name.
- [ ] Typing "smil" shows one block of results without the bar; "zzz" shows "No matches"; clearing the text brings the sections back at the top.
- [ ] Enter or a click on an emoji closes the popup, puts it on the clipboard and pastes it with auto-paste on; it does not appear in the history; reopening the popup shows it first under Recent.
- [ ] Ctrl+1 and Ctrl+P on the Emoji tab do nothing. "Last used tab" reopens on Emoji.
- [ ] Farsi: the tab, the headings and the bar tooltips are translated; the grid is still left to right.
- [ ] The emoji draw in colour, in light and dark, and the popup never takes the focus from the app in front.
