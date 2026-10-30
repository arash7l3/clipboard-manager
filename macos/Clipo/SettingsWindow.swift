import AppKit
import SwiftUI

/// The Settings window: made on first use, then shown again each time.
@MainActor
final class SettingsWindowController {
    private var window: NSWindow?

    /// Brings Settings to the front. Clipo has no Dock icon, so it activates
    /// itself first.
    func show() {
        let window = window ?? makeWindow()
        self.window = window
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
    }

    private func makeWindow() -> NSWindow {
        let window = NSWindow(contentViewController: NSHostingController(rootView: SettingsView()))
        window.title = String(localized: "Clipo Settings", comment: "Title of the Settings window")
        window.styleMask = [.titled, .closable, .miniaturizable]
        window.isReleasedWhenClosed = false
        window.center()
        return window
    }
}
