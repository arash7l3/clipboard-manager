import AppKit
import Carbon.HIToolbox
import SwiftUI

/// Light, dark, or whatever the system uses.
enum AppAppearance: String, CaseIterable, Identifiable, Sendable {
    case system
    case light
    case dark

    var id: String { rawValue }

    /// What to set on `NSApplication.appearance`; `nil` follows the system.
    var nsAppearance: NSAppearance? {
        switch self {
        case .system: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
    }

    var title: LocalizedStringKey {
        switch self {
        case .system: "Match System"
        case .light: "Light"
        case .dark: "Dark"
        }
    }
}

/// The language of Clipo's own text.
enum AppLanguage: String, CaseIterable, Identifiable, Sendable {
    case system
    case english = "en"
    case farsi = "fa"

    var id: String { rawValue }

    /// The language code for `AppleLanguages`; `nil` follows the system.
    var code: String? {
        self == .system ? nil : rawValue
    }
}

/// The shortcut that opens the popup: always the V key, with one of a few
/// modifier sets. A fixed list cannot clash with itself or ask for a key no
/// Mac has.
enum HotkeyChoice: String, CaseIterable, Identifiable, Sendable {
    case commandShift
    case controlShift
    case optionShift
    case commandOption
    case controlOption

    var id: String { rawValue }

    var modifiers: NSEvent.ModifierFlags {
        switch self {
        case .commandShift: [.command, .shift]
        case .controlShift: [.control, .shift]
        case .optionShift: [.option, .shift]
        case .commandOption: [.command, .option]
        case .controlOption: [.control, .option]
        }
    }

    var combo: KeyCombo {
        KeyCombo(keyCode: UInt32(kVK_ANSI_V), character: "v", modifiers: modifiers)
    }

    /// The shortcut as the menus write it, such as "⌘⇧V".
    var label: String {
        var text = ""
        if modifiers.contains(.control) { text += "⌃" }
        if modifiers.contains(.option) { text += "⌥" }
        if modifiers.contains(.shift) { text += "⇧" }
        if modifiers.contains(.command) { text += "⌘" }
        return text + "V"
    }
}

/// How Clipo looks and talks, from the user's settings.
enum InterfaceSettings {
    static let appearanceKey = "appearance"
    static let languageKey = "interfaceLanguage"
    static let hotkeyKey = "hotkey"

    /// The key macOS reads at launch to pick the app's language.
    static let appleLanguagesKey = "AppleLanguages"

    /// The language choice when Clipo started. macOS applies a new one only
    /// at the next launch, so Settings compares against this to offer a
    /// restart. Read once at launch to pin it.
    static let launchLanguage: AppLanguage = language()

    static func appearance(defaults: UserDefaults = .standard) -> AppAppearance {
        defaults.string(forKey: appearanceKey).flatMap(AppAppearance.init(rawValue:)) ?? .system
    }

    static func language(defaults: UserDefaults = .standard) -> AppLanguage {
        defaults.string(forKey: languageKey).flatMap(AppLanguage.init(rawValue:)) ?? .system
    }

    static func hotkey(defaults: UserDefaults = .standard) -> HotkeyChoice {
        defaults.string(forKey: hotkeyKey).flatMap(HotkeyChoice.init(rawValue:)) ?? .commandShift
    }

    /// Makes the next launch use `language`. A system choice removes the
    /// override so the system's language order applies again.
    static func applyLanguage(_ language: AppLanguage, defaults: UserDefaults = .standard) {
        if let code = language.code {
            defaults.set([code], forKey: appleLanguagesKey)
        } else {
            defaults.removeObject(forKey: appleLanguagesKey)
        }
    }

    /// Quits Clipo and opens a new copy a moment later. If the helper cannot
    /// start, Clipo keeps running.
    @MainActor
    static func relaunch() {
        let helper = Process()
        helper.executableURL = URL(fileURLWithPath: "/bin/sh")
        // The path is the script's `$0`, so it is never parsed as shell code.
        helper.arguments = ["-c", "sleep 1; /usr/bin/open \"$0\"", Bundle.main.bundlePath]
        do {
            try helper.run()
        } catch {
            return
        }
        NSApp.terminate(nil)
    }
}
