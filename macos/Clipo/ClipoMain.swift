import AppKit

/// Starts Clipo as a menu-bar app: no Dock icon and no main window
/// (`LSUIElement` is set in the generated Info.plist).
@main
enum ClipoMain {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        // `NSApplication.delegate` is weak; keep ours alive while the app runs.
        withExtendedLifetime(delegate) {
            app.run()
        }
    }
}
