import ClipoCore
import Foundation

/// Which captures are refused, from the user's settings.
///
/// The core keeps these rules in memory only, so the app sets them every time
/// it opens the history.
enum CaptureSettings {
    /// Key in `UserDefaults` of the user's own ignored apps: an array of bundle
    /// identifiers. The Settings window edits it.
    static let ignoredAppsKey = "ignoredApps"
    /// Key in `UserDefaults`: a Bool, capture paused while on.
    static let privateModeKey = "privateMode"

    /// Password managers and the like, ignored out of the box. Most of them
    /// also mark what they copy as concealed; this covers the ones that don't.
    /// Bundle identifiers are matched ignoring case.
    static let defaultIgnoredApps = [
        "com.1password.1password",
        "com.agilebits.onepassword7",
        "com.agilebits.onepassword-osx",
        "com.bitwarden.desktop",
        "org.keepassxc.keepassxc",
        "com.dashlane.dashlanephonefinal",
        "in.sinew.Enpass-Desktop",
        "me.proton.pass.electron",
        "com.apple.keychainaccess",
        "com.apple.Passwords"
    ]

    /// The built-in list followed by the user's additions, without repeats.
    static func ignoredApps(defaults: UserDefaults = .standard) -> [String] {
        let own = defaults.stringArray(forKey: ignoredAppsKey) ?? []
        var seen = Set<String>()
        return (defaultIgnoredApps + own).filter { seen.insert($0.lowercased()).inserted }
    }

    /// Only the apps the user added.
    static func ownIgnoredApps(defaults: UserDefaults = .standard) -> [String] {
        defaults.stringArray(forKey: ignoredAppsKey) ?? []
    }

    /// Adds `identifier` to the user's ignored apps. Returns false, changing
    /// nothing, when it is blank or the built-in list or the user's already
    /// has it.
    @discardableResult
    static func addIgnoredApp(_ identifier: String, defaults: UserDefaults = .standard) -> Bool {
        let trimmed = identifier.trimmingCharacters(in: .whitespacesAndNewlines)
        let known = Set(ignoredApps(defaults: defaults).map { $0.lowercased() })
        guard !trimmed.isEmpty, !known.contains(trimmed.lowercased()) else { return false }
        defaults.set(ownIgnoredApps(defaults: defaults) + [trimmed], forKey: ignoredAppsKey)
        return true
    }

    static func removeIgnoredApp(_ identifier: String, defaults: UserDefaults = .standard) {
        let kept = ownIgnoredApps(defaults: defaults).filter { $0.caseInsensitiveCompare(identifier) != .orderedSame }
        defaults.set(kept, forKey: ignoredAppsKey)
    }

    /// The rules to give the core. Private mode and auto-clear are off unless
    /// the user turned them on.
    static func rules(defaults: UserDefaults = .standard) -> CaptureRules {
        CaptureRules(
            privateMode: defaults.bool(forKey: privateModeKey),
            ignoredApps: ignoredApps(defaults: defaults),
            autoClearAfter: HistorySettings.autoClearAfter(defaults: defaults)
        )
    }

    /// Whether a copy made in `app` may be read at all under `rules`: the
    /// check the watcher runs before touching the pasteboard's data. The core
    /// applies the same rules again when a capture is recorded.
    static func allowsReading(from app: SourceApp?, under rules: CaptureRules) -> Bool {
        guard !rules.privateMode else { return false }
        guard let id = app?.id else { return true }
        return !rules.ignoredApps.contains { $0.caseInsensitiveCompare(id) == .orderedSame }
    }
}
