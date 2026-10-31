# macOS app verification (step 2.6)

Phase 2 exit check: "works on a clean Mac in LTR and RTL, light and dark, keyboard and mouse" ([BUILD-GUIDE](../BUILD-GUIDE.md)).

## What was checked without a Mac

This step ran on a Linux machine with no Xcode, so the app was **not launched**. The code was audited and the gaps found were fixed, with tests that CI runs on the Mac runner.

| Area | Result |
|---|---|
| RTL layout | Popup, rows and Settings use leading/trailing constraints and SwiftUI stacks, so they mirror. The popup opens on the pointer's leading side (tested in `PopupPlacementTests`). A new test checks a row mirrors (`PopupAccessibilityTests`). |
| Farsi strings | Every string in the code is in `Localizable.xcstrings` with a Farsi translation, including the plural. The new "Categories" label was added. |
| Dark mode | Only semantic colors (`labelColor`, `secondaryLabelColor`) and a `.popover` vibrancy background; no fixed colors. Appearance setting sets `NSApp.appearance`. |
| Screen reader | Search field, list and menu-bar icon were labeled. **Fixed:** the tabs had no label; each row was read as several elements and is now one. Selection changes are announced. |
| Keyboard | Everything in the popup works from the search field (arrows, Page keys, Home/End, Tab, Enter, Esc). Settings is reachable from the menu. |

## Still to do on a Mac

Run `cargo xtask package macos`, open `macos/Clipo.xcodeproj`, run, then tick each item.

**English, light mode**
- [ ] Menu-bar icon appears; menu items work.
- [ ] Copy text, an image, files and a link; each shows the right icon and details in the popup.
- [ ] Cmd+Shift+V opens the popup at the pointer; search, tabs, arrows, Enter, Esc, click outside.
- [ ] Choosing an item restores it; with auto-paste on and Accessibility granted it pastes, with it denied the explanation shows once.
- [ ] Settings: login item, history size, auto-clear, private mode, ignore list, shortcut.

**Dark mode**: switch Settings > Appearance to Dark, and the system to Dark with "Match System". Popup, rows and Settings stay readable; thumbnails and the selection highlight look right.

**Farsi (RTL)**: set Language to Farsi, restart.
- [ ] Popup and Settings are mirrored; tab order and icon sides look right.
- [ ] Farsi text copied from another app shows correctly and aligned in rows and search.
- [ ] Mixed Farsi/English and numbers display in the right order.
- [ ] Popup opens to the pointer's left.

**VoiceOver (Cmd+F5)**
- [ ] Popup is announced; search field, tabs and list are named.
- [ ] Moving with the arrows reads each row once.
- [ ] Settings controls all have names.

Record anything that fails as a new step in `PLAN.md`.

## Extras (step 4.2), still to try on a Mac

- [ ] Cmd+1 to Cmd+9 in the popup pick the matching row; rows show "⌘1" and so on.
- [ ] Option+Return (and Option+click) on a rich-text item pastes it without formatting; on an image it beeps.
- [ ] Settings > Snippets: add, edit, delete; the menu-bar Snippets submenu follows and pasting one works.
- [ ] Settings > Data: Export warns, writes a file; Import of that file reports the counts, a second import adds nothing; a random file is refused.
- [ ] Farsi: new tabs and messages are translated and mirrored.

## Pinning (step 4.4), still to try on a Mac

- [ ] Cmd+P pins the selected row (pin mark appears); again unpins it; the Pinned tab lists it.
- [ ] Right-click a row: the menu says Pin or Unpin, and the popup stays open while it is shown.
- [ ] Unpinning in the Pinned tab removes the row; Farsi labels are translated.

## Start tab and private mode (step 4.5), still to try on a Mac

- [ ] Settings > General > Open history on: each choice opens the popup on that tab; "Last used tab" reopens the tab you left.
- [ ] Menu-bar menu > Private Mode toggles the check mark, matches the Settings > Privacy check box, and turns the icon orange with a crossed-out eye (and back).
- [ ] While private mode is on the popup shows the orange note, the list sits below it, and copies are not recorded.
- [ ] Farsi: the new labels are translated and the note is not cut off.

## Clearing (step 4.6), still to try on a Mac

- [ ] The trash button beside the tabs: one click shows a red "Clear?" and the popup stays open; a second click within 3 s clears the tab; waiting, switching tabs or closing the popup puts the trash icon back.
- [ ] Clearing Images (or Text, Files) leaves the other kinds and every pinned item; the Pinned tab has no button.
- [ ] Menu-bar menu > Clear History… asks first; Cancel changes nothing; Clear History leaves only pinned items and the snippets, and the item count in the menu updates.
- [ ] Farsi: the tooltips, "Clear?" and the alert are translated, and the button sits at the left end of the tab row.
