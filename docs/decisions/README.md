# Decision records

Short records of project decisions. Each states the choice, the reason and what it costs. Change a decision by adding a new record that supersedes the old one.

| # | Decision |
|---|---|
| [0001](0001-app-name.md) | App name: Clipo |
| [0002](0002-minimum-os.md) | Minimum OS: macOS 14, Windows 11 |
| [0003](0003-windows-ui-framework.md) | Windows UI: WinUI 3, WPF as fallback |
| [0004](0004-defaults.md) | Defaults: hotkey, history size, auto-paste |
| [0005](0005-repo-layout.md) | One monorepo |
| [0006](0006-signing.md) | Unsigned builds until certificates exist |
| [0007](0007-storage-encryption.md) | Storage encryption: SQLCipher, key in the OS credential store |
| [0008](0008-bindings-and-packaging.md) | Bindings: UniFFI 0.31 in a separate crate, packaged by a Rust xtask |
| [0009](0009-macos-app-shell.md) | macOS app: AppKit shell, core as a local package, login keychain |
| [0010](0010-macos-clipboard-capture.md) | macOS capture: change-count polling, concealed data never read |
| [0011](0011-macos-popup.md) | macOS popup: Carbon hot key, non-activating panel, previews from the core |
| [0012](0012-macos-restore-and-auto-paste.md) | macOS restore and auto-paste: write back what was copied, Cmd+V only if allowed |
| [0013](0013-macos-settings.md) | macOS settings: SwiftUI window over `UserDefaults`, login item, language by relaunch |
| [0014](0014-windows-app-shell.md) | Windows app: WinUI 3 unpackaged, core as a class library, Credential Manager key |
| [0015](0015-windows-capture-tray-hotkey.md) | Windows capture, tray icon and shortcut: clipboard listener, native tray, RegisterHotKey |
| [0016](0016-windows-popup-and-restore.md) | Windows popup, restore and auto-paste: a WinUI window that takes the keyboard, SendInput for Ctrl+V |
| [0017](0017-windows-settings.md) | Windows settings: a WinUI window over a JSON file, Run-key login item, language by restart |
| [0018](0018-snippets-and-export.md) | Extras in the core: snippets table, plain-text view, unencrypted binary export |
| [0019](0019-macos-extras.md) | macOS extras: Option for plain text, Cmd+digit quick paste, snippets in the menu |
| [0020](0020-windows-extras.md) | Windows extras: Shift for plain text, Ctrl+digit quick paste, snippets in the tray menu |
| [0021](0021-pinning.md) | Pinning from the popup: Cmd/Ctrl+P and a row context menu |
| [0022](0022-start-tab-and-private-indicator.md) | Start tab setting, and private mode in the menus with a visible indicator |
| [0023](0023-macos-dmg-and-signing.md) | macOS dmg: one script, Developer ID when available, ad-hoc until then |
| [0024](0024-clearing-a-tab.md) | Clearing a tab: a Clear button with inline confirmation, and Clear History in the menus |
| [0025](0025-windows-msix.md) | Windows MSIX: built by the app's own project, signed afterwards, test-signed until there is a certificate |
| [0026](0026-security-review.md) | Security review: history out of backups, forks off the Mac, clean signed builds, export stays unencrypted |
| [0027](0027-github-releases.md) | Releases: a `v*` tag attaches the dmg and the MSIX to a GitHub release |
| [0028](0028-app-icon-file-private-badge-tray-focus.md) | macOS icon as an .icns file, a badge for private mode, and the tray menu's focus on Windows |
| [0029](0029-stable-signature-build-number-snippet-history.md) | A stable self-signed signature, a build number per release, and snippets in the history |
| [0030](0030-macos-file-references-on-restore.md) | macOS keeps copied files as plain paths and never empties the clipboard on restore |
| [0031](0031-paste-target-and-screenshots.md) | Pasting into the app that was in front, and screenshots in the history |
| [0032](0032-windows-popup-keeps-the-focus.md) | Windows: the popup leaves the focus where the user types, and picked images paste into folders |
