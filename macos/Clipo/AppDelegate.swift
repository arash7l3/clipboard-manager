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
    private var popup: PopupController?
    private var hotkey: GlobalHotkey?
    private let settings = SettingsCoordinator()
    private let settingsWindow = SettingsWindowController()
    private var explainedAccessibility = false

    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "capture")

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Unit tests are hosted in the app. They build what they test
        // themselves and must not touch the user's keychain or history.
        guard ProcessInfo.processInfo.environment["XCTestConfigurationFilePath"] == nil else { return }

        statusMenu.onRetry = { [weak self] in self?.openHistory() }
        statusMenu.onShowHistory = { [weak self] in self?.togglePopup() }
        statusMenu.onShowSettings = { [weak self] in self?.settingsWindow.show() }
        statusItem = statusMenu.install()

        let hotkey = GlobalHotkey { [weak self] in self?.togglePopup() }
        let combo = InterfaceSettings.hotkey().combo
        statusMenu.showShortcut(hotkey.register(combo) ? combo : nil)
        self.hotkey = hotkey

        settings.onHotkeyChange = { [weak self] choice in self?.moveHotkey(to: choice) ?? false }
        settings.onHistoryTrimmed = { [weak self] count in self?.historyChanged(itemCount: count) }
        settings.start()

        openHistory()
    }

    func applicationWillTerminate(_ notification: Notification) {
        hotkey?.unregister()
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
                popup = makePopup(for: store)
                statusMenu.show(.ready(itemCount: itemCount))
                startCapturing(into: store)
                settings.attach(store)
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

        recording = Task { [weak self] in
            for await change in watcher.changes {
                // One change at a time keeps the history in copy order.
                let outcome = await Task.detached(priority: .utility) { () -> (changed: Bool, itemCount: UInt64?) in
                    switch ClipboardRecorder.record(change, in: store) {
                    case .added?: (true, try? store.count())
                    case .bumped?: (true, nil)
                    default: (false, nil)
                    }
                }.value
                if outcome.changed {
                    self?.historyChanged(itemCount: outcome.itemCount)
                }
            }
        }
    }

    /// After a copy was stored or the settings trimmed the history.
    /// `itemCount` is the new count when it is known, `nil` when an existing
    /// item only moved to the top.
    private func historyChanged(itemCount: UInt64?) {
        if let itemCount {
            statusMenu.show(.ready(itemCount: itemCount))
        }
        popup?.historyChanged()
    }

    /// Moves the global shortcut. When another app holds the new one, the
    /// old one is registered again and the caller is told it failed.
    private func moveHotkey(to combo: KeyCombo) -> Bool {
        guard let hotkey else { return false }
        let previous = hotkey.combo
        if hotkey.register(combo) {
            statusMenu.showShortcut(combo)
            return true
        }
        NSSound.beep()
        let restored = previous.flatMap { hotkey.register($0) ? $0 : nil }
        statusMenu.showShortcut(restored)
        return false
    }

    private func makePopup(for store: ClipStore) -> PopupController {
        popup?.dismiss()
        let model = PopupModel { query in History.previews(query, in: store) }
        let popup = PopupController(model: model)
        let restorer = ItemRestorer()
        restorer.load = { id in History.item(id, in: store) }
        restorer.bump = { id in History.bump(id, in: store) }
        restorer.didWrite = { [weak self] in self?.watcher?.skipCurrent() }
        popup.onChoose = { [weak self] row in
            Task { await self?.restore(row, with: restorer) }
        }
        return popup
    }

    /// Puts the picked item on the clipboard. When auto-paste is on but macOS
    /// has not given Clipo the Accessibility permission, the item stays on the
    /// clipboard and the user is told why nothing was pasted.
    private func restore(_ row: ItemPreview, with restorer: ItemRestorer) async {
        switch await restorer.restore(row) {
        case .restored, .pasted:
            break
        case .needsAccessibility:
            // Once per launch: afterwards the user knows, and the item is
            // on the clipboard either way.
            guard !explainedAccessibility else { return }
            explainedAccessibility = true
            Accessibility.explain()
        case .unavailable:
            NSSound.beep()
        }
    }

    /// Opens or closes the popup; beeps while the history is not open.
    private func togglePopup() {
        guard let popup else {
            NSSound.beep()
            return
        }
        popup.toggle()
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
