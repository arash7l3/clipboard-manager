import AppKit
import ClipoCore

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

    /// Pause between the popup closing and Cmd+V: the app underneath has to
    /// become the key window again first.
    static let pasteDelay: Duration = .milliseconds(120)

    /// Loads a full item by id; does disk I/O, so it runs off the main thread.
    var load: @Sendable (Int64) -> ClipItem? = { _ in nil }
    /// Moves an item to the top of the history, as if it was copied again.
    var bump: @Sendable (Int64) -> Void = { _ in }
    /// Called right after the pasteboard was written, so the watcher does not
    /// record Clipo's own write as a new copy.
    var didWrite: @MainActor () -> Void = {}
    var autoPasteEnabled: () -> Bool = { PasteSettings.autoPaste() }
    var hasAccessibility: () -> Bool = { Accessibility.isGranted }
    var sendPaste: @MainActor () -> Void = { Accessibility.sendPaste() }

    private let pasteboard: NSPasteboard

    init(pasteboard: NSPasteboard = .general) {
        self.pasteboard = pasteboard
    }

    /// Restores the full item behind `preview`.
    func restore(_ preview: ItemPreview) async -> Outcome {
        let id = preview.id
        let load = self.load
        guard let item = await Task.detached(priority: .userInitiated, operation: { load(id) }).value,
              PasteboardWriter.write(item.content, to: pasteboard)
        else { return .unavailable }
        didWrite()

        let bump = self.bump
        Task.detached(priority: .utility) { bump(id) }

        guard autoPasteEnabled() else { return .restored }
        guard hasAccessibility() else { return .needsAccessibility }
        try? await Task.sleep(for: Self.pasteDelay)
        sendPaste()
        return .pasted
    }
}
