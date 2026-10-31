import ClipoCore
import Foundation
import Observation
import os

private let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "extras")

/// Snippets and export / import, on top of the open history. Settings shows
/// it and the menu-bar menu lists its snippets. The calls do disk I/O off the
/// main thread.
@MainActor
@Observable
final class ExtrasModel {
    /// What importing came to.
    enum ImportResult: Equatable {
        case imported(ImportSummary)
        /// Not a Clipo export, or damaged.
        case invalid
        /// Written by a newer Clipo.
        case tooNew
        case failed
    }

    /// Saved snippets, in creation order.
    private(set) var snippets: [Snippet] = []
    /// The history is open, so snippets can be read and saved.
    private(set) var isAvailable = false

    /// Called after ``snippets`` changed.
    @ObservationIgnored var onSnippetsChange: (@MainActor () -> Void)?
    /// Called with the new item count after an import changed the history.
    @ObservationIgnored var onImported: (@MainActor (UInt64?) -> Void)?

    @ObservationIgnored private var store: ClipStore?

    /// Starts using `store` and loads its snippets.
    func attach(_ store: ClipStore) async {
        self.store = store
        isAvailable = true
        await reloadSnippets()
    }

    func reloadSnippets() async {
        guard let store else { return }
        let found = await Task.detached(priority: .userInitiated) { () -> [Snippet]? in
            do {
                return try store.snippets(text: "")
            } catch {
                logger.error("listing snippets failed: \(String(describing: error), privacy: .public)")
                return nil
            }
        }.value
        if let found {
            snippets = found
            onSnippetsChange?()
        }
    }

    /// Adds a snippet, or changes the one with `id`. Returns false if the
    /// title is blank, the text empty, or the store failed.
    func save(id: Int64?, title: String, text: String) async -> Bool {
        guard let store else { return false }
        let saved = await Task.detached(priority: .userInitiated) { () -> Bool in
            do {
                if let id {
                    _ = try store.updateSnippet(id: id, title: title, body: text, at: Date())
                } else {
                    _ = try store.addSnippet(title: title, body: text, at: Date())
                }
                return true
            } catch {
                logger.error("saving a snippet failed: \(String(describing: error), privacy: .public)")
                return false
            }
        }.value
        if saved {
            await reloadSnippets()
        }
        return saved
    }

    func delete(_ id: Int64) async {
        guard let store else { return }
        await Task.detached(priority: .userInitiated) {
            do {
                try store.deleteSnippet(id: id)
            } catch {
                logger.error("deleting a snippet failed: \(String(describing: error), privacy: .public)")
            }
        }.value
        await reloadSnippets()
    }

    /// The history and snippets as one unencrypted blob, or `nil` on failure.
    func exportData() async -> Data? {
        guard let store else { return nil }
        return await Task.detached(priority: .userInitiated) { () -> Data? in
            do {
                return try store.exportData()
            } catch {
                logger.error("export failed: \(String(describing: error), privacy: .public)")
                return nil
            }
        }.value
    }

    func importData(_ data: Data) async -> ImportResult {
        guard let store else { return .failed }
        let result = await Task.detached(priority: .userInitiated) { () -> ImportResult in
            do {
                return .imported(try store.importData(data: data))
            } catch let error as ClipoError {
                switch error {
                case .InvalidExport: return .invalid
                case .UnsupportedExport: return .tooNew
                default: break
                }
                logger.error("import failed: \(String(describing: error), privacy: .public)")
                return .failed
            } catch {
                return .failed
            }
        }.value
        if case .imported = result {
            await reloadSnippets()
            onImported?(await Task.detached(priority: .utility) { try? store.count() }.value)
        }
        return result
    }
}
