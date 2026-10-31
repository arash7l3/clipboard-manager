# 0009 — macOS app: AppKit shell, core as a local package, login keychain

Status: accepted

**Decision.**

- `macos/Clipo.xcodeproj` has two targets: the app `Clipo` and its hosted unit tests `ClipoTests` (Swift Testing). Both build in Swift 6 language mode with warnings treated as errors. Folders are file-system synchronized (Xcode 16 project format): a Swift file added to `macos/Clipo` or `macos/ClipoTests` is built without editing the project.
- The app starts from an AppKit `@main`, has `LSUIElement` set (no Dock icon, no main window) and shows one `NSStatusItem` with a menu.
- The core is linked as the local Swift package `core/dist/apple/ClipoCore` that `cargo xtask package macos` writes (decision 0008). Run it before opening or building the project.
- The history is `~/Library/Application Support/Clipo/history.db`, in a folder only the user can read and left out of backups (decision 0026). Its key is a generic password in the login keychain, service `io.github.arash7l3.Clipo`, account `history-key`.
- The bundle identifier is `io.github.arash7l3.Clipo`. The app is not sandboxed and uses the hardened runtime.

**Why.**

- AppKit gives full control of the status item and, later, the popup panel. SwiftUI's `MenuBarExtra` cannot do both.
- A local package keeps the generated code and the static library out of the repository, and Xcode builds against whatever the xtask last packaged.
- The data protection keychain (this-device-only, accessible after first unlock) needs a signed app with a keychain entitlement, which we don't have yet (decision 0006). The login keychain works unsigned and is never synced to iCloud.
- Auto-paste (step 2.4) posts keyboard events to other apps, which the App Sandbox does not allow.

**Safeguards.**

- A missing or wrong key never deletes anything by itself. The app asks, and only then deletes the database and the key and starts a new, empty history.
- A keychain failure other than "not found" (locked keychain, denied prompt) is reported as unavailable with "Try Again", not as a missing key, so it cannot lead to deleting the history.
- Unit tests run inside the app, but the app skips its launch work when started by XCTest, so tests never touch the user's keychain or history.

**Consequences.**

- Unsigned builds change identity on every rebuild, so macOS asks again before giving a new build the key ("Always Allow" stops it until the next build). Moving to the data protection keychain, with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`, is part of step 5.2 and needs a one-time key migration.
- Xcode 16 or later is needed to open the project.
- CI packages the core in the macOS job before building, because the checkout removes `core/dist/`.
