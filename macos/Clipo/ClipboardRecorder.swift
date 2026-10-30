import ClipoCore
import Foundation
import os

/// Turns a clipboard change into a stored history item.
enum ClipboardRecorder {
    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "capture")

    /// Records `change` in `store`. Returns what the store did, or `nil` if the
    /// change was not worth keeping or storing failed. Does image decoding and
    /// disk I/O: call it off the main thread.
    static func record(_ change: PasteboardChange, in store: ClipStore) -> Recorded? {
        let capture = PasteboardReader.capture(
            from: change.content,
            sourceApp: change.sourceApp,
            at: change.date,
            maxItemBytes: Int(clamping: store.limits().maxItemBytes)
        )
        guard let capture else { return nil }
        do {
            return try store.record(capture: capture)
        } catch {
            // Core errors carry codes and ids, never clipboard content.
            logger.error("recording a capture failed: \(String(describing: error), privacy: .public)")
            return nil
        }
    }
}
