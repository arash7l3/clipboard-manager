import ClipoCore
import Foundation

/// Which tab the popup opens on.
enum StartTab: String, CaseIterable, Identifiable, Sendable {
    case all
    case text
    case images
    case files
    case pinned
    /// The tab the popup was on when it was last open, until Clipo quits.
    case lastUsed

    var id: String { rawValue }

    /// The tab to open on. `lastUsed` is the tab the popup was on when it was
    /// last open, for ``lastUsed``.
    func category(lastUsed: ClipoCore.Category) -> ClipoCore.Category {
        switch self {
        case .all: .all
        case .text: .text
        case .images: .images
        case .files: .files
        case .pinned: .pinned
        case .lastUsed: lastUsed
        }
    }

    var title: String {
        switch self {
        case .all: PopupText.tabTitle(.all)
        case .text: PopupText.tabTitle(.text)
        case .images: PopupText.tabTitle(.images)
        case .files: PopupText.tabTitle(.files)
        case .pinned: PopupText.tabTitle(.pinned)
        case .lastUsed: String(localized: "Last used tab", comment: "Start tab choice: the tab that was last open")
        }
    }
}

/// How the popup opens, from the user's settings.
enum PopupSettings {
    /// Key in `UserDefaults`: a ``StartTab`` raw value.
    static let startTabKey = "popupStartTab"

    static func startTab(defaults: UserDefaults = .standard) -> StartTab {
        defaults.string(forKey: startTabKey).flatMap(StartTab.init(rawValue:)) ?? .all
    }
}
