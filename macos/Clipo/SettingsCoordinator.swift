import AppKit
import ClipoCore

/// What the Settings window changes while Clipo runs.
struct SettingsSnapshot: Equatable, Sendable {
    /// What decides which copies are kept and for how long.
    struct Retention: Equatable, Sendable {
        var maxItems: UInt64
        var autoClearAfter: TimeInterval?
        var privateMode: Bool
        var ignoredApps: [String]
    }

    var appearance: AppAppearance
    var hotkey: HotkeyChoice
    var history: Retention

    init(defaults: UserDefaults = .standard) {
        appearance = InterfaceSettings.appearance(defaults: defaults)
        hotkey = InterfaceSettings.hotkey(defaults: defaults)
        history = Retention(
            maxItems: HistorySettings.maxItems(defaults: defaults),
            autoClearAfter: HistorySettings.autoClearAfter(defaults: defaults),
            privateMode: defaults.bool(forKey: CaptureSettings.privateModeKey),
            ignoredApps: CaptureSettings.ignoredApps(defaults: defaults)
        )
    }
}

/// Applies the user's settings when they change, and keeps the history
/// trimmed to them: once an hour, and right after a change.
@MainActor
final class SettingsCoordinator {
    /// Asked to move the shortcut. Returns false if it could not, in which
    /// case the setting goes back to the one before.
    var onHotkeyChange: (@MainActor (KeyCombo) -> Bool)?
    /// Called with the new state when private mode was turned on or off.
    var onPrivateModeChange: (@MainActor (Bool) -> Void)?
    /// Called with the new item count after the history was trimmed.
    var onHistoryTrimmed: (@MainActor (UInt64?) -> Void)?

    private let defaults: UserDefaults
    private var applied: SettingsSnapshot
    private var store: ClipStore?
    private var observer: NSObjectProtocol?
    private var hourly: Task<Void, Never>?

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        applied = SettingsSnapshot(defaults: defaults)
    }

    /// Applies the appearance and starts watching for changes.
    func start() {
        _ = InterfaceSettings.launchLanguage
        NSApp.appearance = applied.appearance.nsAppearance
        observer = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification,
            object: defaults,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.defaultsChanged() }
        }
        hourly = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(3600))
                await self?.trim()
            }
        }
    }

    /// Starts keeping `store` to the settings, beginning now.
    func attach(_ store: ClipStore) {
        self.store = store
        Task { await trim() }
    }

    /// Applies what differs from `applied`. Every write to `UserDefaults`
    /// lands here, most of them for settings that are not Clipo's.
    func defaultsChanged() {
        let current = SettingsSnapshot(defaults: defaults)
        let before = applied
        guard current != before else { return }
        applied = current

        if current.appearance != before.appearance {
            NSApp.appearance = current.appearance.nsAppearance
        }
        if current.hotkey != before.hotkey, onHotkeyChange?(current.hotkey.combo) == false {
            applied.hotkey = before.hotkey
            defaults.set(before.hotkey.rawValue, forKey: InterfaceSettings.hotkeyKey)
        }
        if current.history.privateMode != before.history.privateMode {
            onPrivateModeChange?(current.history.privateMode)
        }
        if current.history != before.history {
            Task { await trim() }
        }
    }

    private func trim() async {
        guard let store else { return }
        let count = await Task.detached(priority: .utility) { () -> UInt64? in
            HistorySettings.apply(to: store)
            return try? store.count()
        }.value
        onHistoryTrimmed?(count)
    }
}
