import ClipoCore
import Foundation

/// Which captures are refused, from the user's settings.
///
/// The core keeps these rules in memory only, so the app sets them every time
/// it opens the history.
enum CaptureSettings {
    /// Key in `UserDefaults` of the user's own ignored apps: an array of bundle
    /// identifiers. The Settings window (step 2.5) edits it.
    static let ignoredAppsKey = "ignoredApps"

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

    /// The rules to give the core. Private mode starts off.
    static func rules(defaults: UserDefaults = .standard) -> CaptureRules {
        CaptureRules(privateMode: false, ignoredApps: ignoredApps(defaults: defaults), autoClearAfter: nil)
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
