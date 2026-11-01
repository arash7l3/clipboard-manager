import AppKit
import ClipoCore
import os

/// Puts the item the user picked back on the clipboard and, if auto-paste is
/// on, pastes it into the app that was in front.
@MainActor
final class ItemRestorer {
    /// What picking an item came to.
    enum Outcome: Equatable {
        /// On the clipboard; the user pastes it.
        case restored
        /// On the clipboard and pasted into the app in front.
        case pasted
        /// On the clipboard. Auto-paste is on but the Accessibility permission
        /// is missing, so nothing was pasted.
        case needsAccessibility
        /// The item could not be loaded or the pasteboard refused it.
        case unavailable
    }

    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "paste")

    /// Pause between the popup closing and Cmd+V: the app underneath has to
    /// become the key window again first.
    static let pasteDelay: Duration = .milliseconds(120)

    /// Loads a full item by id; does disk I/O, so it runs off the main thread.
    var load: @Sendable (Int64) -> ClipItem? = { _ in nil }
    /// Loads an item's plain text by id (`nil` for an image); does disk I/O,
    /// so it runs off the main thread.
    var loadPlainText: @Sendable (Int64) -> String? = { _ in nil }
    /// Moves an item to the top of the history, as if it was copied again.
    var bump: @Sendable (Int64) -> Void = { _ in }
    /// Called right after the pasteboard was written, so the watcher does not
    /// record Clipo's own write as a new copy.
    var didWrite: @MainActor () -> Void = {}
    var autoPasteEnabled: () -> Bool = { PasteSettings.autoPaste() }
    var hasAccessibility: () -> Bool = { Accessibility.isGranted }
    var sendPaste: @MainActor () -> Void = { Accessibility.sendPaste() }
    /// Lets the app in front resign and regain active status before a file
    /// or an image is pasted, so it reads the pasteboard again.
    var refreshTarget: @MainActor () async -> Void = { await Accessibility.reactivateFrontmostApp() }

    private let pasteboard: NSPasteboard

    init(pasteboard: NSPasteboard = .general) {
        self.pasteboard = pasteboard
    }

    /// Restores the full item behind `preview`, or only its plain text when
    /// `plainText` is set. An image has no plain text and is `.unavailable`.
    func restore(_ preview: ItemPreview, plainText: Bool = false) async -> Outcome {
        let id = preview.id
        var isText = true
        if plainText {
            let load = loadPlainText
            guard let text = await Task.detached(priority: .userInitiated, operation: { load(id) }).value,
                  writeString(text)
            else { return .unavailable }
        } else {
            let load = self.load
            guard let item = await Task.detached(priority: .userInitiated, operation: { load(id) }).value else {
                return .unavailable
            }
            isText = item.content.isText
            guard PasteboardWriter.write(item.content, to: pasteboard) else {
                // An id only, never the item's content.
                Self.logger.error("the pasteboard did not take item \(id, privacy: .public)")
                return .unavailable
            }
        }
        didWrite()

        let bump = self.bump
        Task.detached(priority: .utility) { bump(id) }
        return await pasteIfWanted(refreshingTarget: !isText)
    }

    /// Puts `text`, for example a snippet, on the clipboard as plain text.
    /// It is not history, so nothing moves to the top.
    func restore(text: String) async -> Outcome {
        guard writeString(text) else { return .unavailable }
        didWrite()
        return await pasteIfWanted()
    }

    private func writeString(_ text: String) -> Bool {
        pasteboard.clearContents()
        return pasteboard.setString(text, forType: .string)
    }

    /// With `refreshingTarget`, the app in front first resigns and regains
    /// active status, whether Clipo pastes or the user does. The popup never
    /// activates Clipo, so the app stays active throughout; Finder then keeps
    /// its old idea of what it can paste, and Cmd+V with files on the
    /// pasteboard did nothing there.
    private func pasteIfWanted(refreshingTarget: Bool = false) async -> Outcome {
        if refreshingTarget {
            await refreshTarget()
        }
        guard autoPasteEnabled() else { return .restored }
        guard hasAccessibility() else { return .needsAccessibility }
        try? await Task.sleep(for: Self.pasteDelay)
        sendPaste()
        return .pasted
    }
}

private extension ItemContent {
    var isText: Bool {
        if case .text = self {
            return true
        }
        return false
    }
}
