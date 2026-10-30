import AppKit
import ClipoCore
import os

/// Owns the menu-bar item and the open history.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let statusMenu = StatusMenu()
    private var statusItem: NSStatusItem?
    private var store: ClipStore?
    private var watcher: PasteboardWatcher?
    private var recording: Task<Void, Never>?

    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "capture")

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
                startCapturing(into: store)
            case .keyProblem:
                statusMenu.show(.unavailable)
                offerNewHistory()
            case .failed:
                statusMenu.show(.unavailable)
            }
        }
    }

    /// Applies the capture rules and records every clipboard change from now
    /// on. Without the rules the ignore list would not hold, so a failure to
    /// set them means nothing is captured.
    private func startCapturing(into store: ClipStore) {
        stopCapturing()
        do {
            try store.setRules(rules: CaptureSettings.rules())
        } catch {
            Self.logger.error("capture rules were refused: \(String(describing: error), privacy: .public)")
            return
        }

        let watcher = PasteboardWatcher(shouldRead: { app in
            CaptureSettings.allowsReading(from: app, under: store.rules())
        })
        self.watcher = watcher
        watcher.start()

        let statusMenu = statusMenu
        recording = Task {
            for await change in watcher.changes {
                // One change at a time keeps the history in copy order.
                let itemCount = await Task.detached(priority: .utility) { () -> UInt64? in
                    guard case .added = ClipboardRecorder.record(change, in: store) else { return nil }
                    return try? store.count()
                }.value
                if let itemCount {
                    statusMenu.show(.ready(itemCount: itemCount))
                }
            }
        }
    }

    private func stopCapturing() {
        watcher?.stop()
        watcher = nil
        recording?.cancel()
        recording = nil
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
