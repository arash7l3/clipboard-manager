import AppKit
import ClipoCore

/// Owns the menu-bar item and the open history.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let statusMenu = StatusMenu()
    private var statusItem: NSStatusItem?
    private var store: ClipStore?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Unit tests are hosted in the app. They build what they test
        // themselves and must not touch the user's keychain or history.
        guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else { return }

        statusMenu.onRetry = { [weak self] in self?.openHistory() }
        statusItem = statusMenu.install()
        openHistory()
    }

    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool {
        true
    }

    private func openHistory() {
        statusMenu.show(.opening)
        Task {
            switch await History.openDefault() {
            case let .ready(store, itemCount):
                self.store = store
                statusMenu.show(.ready(itemCount: itemCount))
            case .keyProblem:
                statusMenu.show(.unavailable)
                offerNewHistory()
            case .failed:
                statusMenu.show(.unavailable)
            }
        }
    }

    /// The core never replaces a key by itself: the user decides whether to
    /// give up the old history.
    private func offerNewHistory() {
        NSApp.activate()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = String(
            localized: "Clipo can't open your clipboard history",
            comment: "Alert title when the history's encryption key is missing or wrong"
        )
        alert.informativeText = String(
            localized: """
            The key that encrypts it is missing from your keychain or doesn't match. \
            You can start a new, empty history. The old history can't be recovered.
            """,
            comment: "Alert text when the history's encryption key is missing or wrong"
        )
        alert.addButton(withTitle: String(
            localized: "Start New History",
            comment: "Alert button that deletes the unreadable history and starts over"
        ))
        alert.addButton(withTitle: String(
            localized: "Not Now",
            comment: "Alert button that keeps the unreadable history for now"
        ))
        alert.buttons.first?.hasDestructiveAction = true
        guard alert.runModal() == .alertFirstButtonReturn else { return }

        Task {
            if await History.startOverDefault() {
                openHistory()
            }
        }
    }
}
