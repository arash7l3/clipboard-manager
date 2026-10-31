import Foundation

/// The popup's Clear button asks twice: the first press arms it, and only a
/// second press within ``window`` clears. Inline, because an alert would take
/// key status from the popup and close it.
struct ClearConfirmation {
    /// How long an armed button waits for the second press, in seconds.
    let window: TimeInterval
    private let clock: () -> Date
    private var armedAt: Date?

    init(window: TimeInterval = 3, clock: @escaping () -> Date = Date.init) {
        self.window = window
        self.clock = clock
    }

    /// The button is waiting for the second press.
    var isArmed: Bool {
        guard let armedAt else { return false }
        return clock().timeIntervalSince(armedAt) < window
    }

    /// A press. Returns `true` when it is the confirming one and the tab
    /// should be cleared.
    mutating func press() -> Bool {
        if isArmed {
            armedAt = nil
            return true
        }
        armedAt = clock()
        return false
    }

    /// Back to the first press, as after switching tabs or closing the popup.
    mutating func disarm() {
        armedAt = nil
    }
}
