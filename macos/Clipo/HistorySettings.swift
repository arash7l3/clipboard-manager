import ClipoCore
import Foundation
import os

/// How much history is kept and for how long, from the user's settings.
enum HistorySettings {
    /// Key in `UserDefaults`: an Int, the most unpinned items kept.
    static let maxItemsKey = "historyLimit"
    /// Key in `UserDefaults`: an Int, days after which unpinned items are
    /// deleted; 0 keeps them.
    static let autoClearDaysKey = "autoClearDays"

    /// Decision 0004; a test keeps it equal to the core's default.
    static let defaultMaxItems = 200
    static let maxItemsChoices = [50, 100, 200, 500, 1000]
    static let autoClearDaysChoices = [0, 1, 7, 30]

    private static let largestMaxItems = 100_000
    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "history")

    static func maxItems(defaults: UserDefaults = .standard) -> UInt64 {
        let stored = defaults.object(forKey: maxItemsKey) as? Int ?? defaultMaxItems
        return UInt64(min(max(stored, 1), largestMaxItems))
    }

    /// `nil` when auto-clear is off.
    static func autoClearAfter(defaults: UserDefaults = .standard) -> TimeInterval? {
        let days = defaults.integer(forKey: autoClearDaysKey)
        return days > 0 ? TimeInterval(days) * 86_400 : nil
    }

    /// Gives the open history the current limits and rules, then deletes what
    /// has expired. Returns whether it worked. Does disk I/O: call it off the
    /// main thread.
    @discardableResult
    static func apply(to store: ClipStore, defaults: UserDefaults = .standard) -> Bool {
        do {
            var limits = store.limits()
            limits.maxItems = maxItems(defaults: defaults)
            try store.setLimits(limits: limits)
            try store.setRules(rules: CaptureSettings.rules(defaults: defaults))
            _ = try store.clearExpired(now: Date())
            return true
        } catch {
            // Core errors carry codes and ids, never clipboard content.
            logger.error("applying the history settings failed: \(String(describing: error), privacy: .public)")
            return false
        }
    }
}
