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
