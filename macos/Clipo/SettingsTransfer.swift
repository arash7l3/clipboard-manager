import ClipoCore
import Foundation

/// The settings an export carries, as key and value text. Keys without a
/// platform prefix mean the same on Windows, so a file from one imports on
/// the other; the shortcut and the ignored apps differ per platform and carry
/// a `macos.` prefix. Unknown keys and values a setting cannot take are
/// skipped on import.
enum SettingsTransfer {
    static let maxItemsKey = "history.maxItems"
    static let autoClearDaysKey = "history.autoClearDays"
    static let privateModeKey = "capture.privateMode"
    static let autoPasteKey = "paste.autoPaste"
    static let appearanceKey = "interface.appearance"
    static let languageKey = "interface.language"
    static let startTabKey = "popup.startTab"
    static let hotkeyKey = "macos.hotkey"
    static let ignoredAppsKey = "macos.ignoredApps"

    /// What a value may be, to check it before it is stored.
    private enum Kind: Sendable {
        case count(minimum: Int)
        case flag
        case choice([String])

        func parse(_ text: String) -> Any? {
            switch self {
            case let .count(minimum):
                guard let number = Int(text), number >= minimum else { return nil }
                return number
            case .flag:
                return Bool(text)
            case let .choice(names):
                return names.contains(text) ? text : nil
            }
        }
    }

    /// The settings kept as one plain `UserDefaults` value: export key to
    /// `UserDefaults` key and what the value may be.
    private static let plain: [String: (defaultsKey: String, kind: Kind)] = [
        maxItemsKey: (HistorySettings.maxItemsKey, .count(minimum: 1)),
        autoClearDaysKey: (HistorySettings.autoClearDaysKey, .count(minimum: 0)),
        privateModeKey: (CaptureSettings.privateModeKey, .flag),
        autoPasteKey: (PasteSettings.autoPasteKey, .flag),
        appearanceKey: (InterfaceSettings.appearanceKey, .choice(AppAppearance.allCases.map(\.rawValue))),
        languageKey: (InterfaceSettings.languageKey, .choice(AppLanguage.allCases.map(\.rawValue))),
        startTabKey: (PopupSettings.startTabKey, .choice(StartTab.allCases.map(\.rawValue))),
        hotkeyKey: (InterfaceSettings.hotkeyKey, .choice(HotkeyChoice.allCases.map(\.rawValue)))
    ]

    /// The current settings as the pairs an export carries.
    static func export(defaults: UserDefaults = .standard) -> [ClipoCore.Setting] {
        let values = [
            maxItemsKey: String(HistorySettings.maxItems(defaults: defaults)),
            autoClearDaysKey: String(max(defaults.integer(forKey: HistorySettings.autoClearDaysKey), 0)),
            privateModeKey: String(defaults.bool(forKey: CaptureSettings.privateModeKey)),
            autoPasteKey: String(PasteSettings.autoPaste(defaults: defaults)),
            appearanceKey: InterfaceSettings.appearance(defaults: defaults).rawValue,
            languageKey: InterfaceSettings.language(defaults: defaults).rawValue,
            startTabKey: PopupSettings.startTab(defaults: defaults).rawValue,
            hotkeyKey: InterfaceSettings.hotkey(defaults: defaults).rawValue,
            ignoredAppsKey: CaptureSettings.ownIgnoredApps(defaults: defaults).joined(separator: "\n")
        ]
        return values.keys.sorted().map { ClipoCore.Setting(key: $0, value: values[$0] ?? "") }
    }

    /// Stores the values `settings` holds. What they do not hold, or hold in
    /// a form this version cannot read, stays as it is. The settings
    /// coordinator sees the change and applies it as if made in Settings.
    static func apply(_ settings: [ClipoCore.Setting], defaults: UserDefaults = .standard) {
        for setting in settings {
            if setting.key == ignoredAppsKey {
                setIgnoredApps(setting.value, defaults: defaults)
            } else if let entry = plain[setting.key], let value = entry.kind.parse(setting.value) {
                defaults.set(value, forKey: entry.defaultsKey)
            }
            // macOS picks the language at launch from its own key.
            if setting.key == languageKey, let language = AppLanguage(rawValue: setting.value) {
                InterfaceSettings.applyLanguage(language, defaults: defaults)
            }
        }
    }

    /// Replaces the user's own ignored apps with the lines of `text`, without
    /// blanks, repeats or apps the built-in list has.
    private static func setIgnoredApps(_ text: String, defaults: UserDefaults) {
        var seen = Set(CaptureSettings.defaultIgnoredApps.map { $0.lowercased() })
        var kept: [String] = []
        for line in text.split(separator: "\n") {
            let app = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if !app.isEmpty, seen.insert(app.lowercased()).inserted {
                kept.append(app)
            }
        }
        defaults.set(kept, forKey: CaptureSettings.ignoredAppsKey)
    }
}
