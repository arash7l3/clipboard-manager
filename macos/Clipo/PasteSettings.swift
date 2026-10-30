import Foundation

/// Whether picking an item also pastes it, from the user's settings.
enum PasteSettings {
    /// Key in `UserDefaults`: a Bool. The Settings window (step 2.5) edits it.
    static let autoPasteKey = "autoPaste"

    /// Off unless the user turned it on: it needs the Accessibility permission
    /// and can paste into the wrong place (decision 0004).
    static func autoPaste(defaults: UserDefaults = .standard) -> Bool {
        defaults.bool(forKey: autoPasteKey)
    }
}
