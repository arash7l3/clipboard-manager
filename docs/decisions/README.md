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
