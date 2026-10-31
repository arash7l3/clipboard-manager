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
