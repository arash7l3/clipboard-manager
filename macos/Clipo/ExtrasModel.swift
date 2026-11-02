import ClipoCore
import Foundation
import Observation
import os

private let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "extras")

/// Snippets and export / import, on top of the open history. Settings shows
/// it and the menu-bar menu lists its snippets. An export also carries the
/// settings, and importing one applies them. The calls do disk I/O off the
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

    /// An import's result and the settings the file carried.
    private typealias Imported = (result: ImportResult, settings: [ClipoCore.Setting])

    @ObservationIgnored private var store: ClipStore?
    /// Where the settings an export carries are read from and written to.
    @ObservationIgnored private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

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

    /// The history with item names, the snippets and the settings as one
    /// unencrypted blob, or `nil` on failure.
    func exportData() async -> Data? {
        guard let store else { return nil }
        let settings = SettingsTransfer.export(defaults: defaults)
        return await Task.detached(priority: .userInitiated) { () -> Data? in
            do {
                return try store.exportState(settings: settings)
            } catch {
                logger.error("export failed: \(String(describing: error), privacy: .public)")
                return nil
            }
        }.value
    }

    /// Adds what `data` holds and applies the settings it carried.
    func importData(_ data: Data) async -> ImportResult {
        guard let store else { return .failed }
        let (result, settings) = await Task.detached(priority: .userInitiated) { () -> Imported in
            do {
                let state = try store.importState(data: data)
                return (.imported(state.summary), state.settings)
            } catch let error as ClipoError {
                switch error {
                case .InvalidExport: return (.invalid, [])
                case .UnsupportedExport: return (.tooNew, [])
                default: break
                }
                logger.error("import failed: \(String(describing: error), privacy: .public)")
                return (.failed, [])
            } catch {
                return (.failed, [])
            }
        }.value
        if case .imported = result {
            SettingsTransfer.apply(settings, defaults: defaults)
            await reloadSnippets()
            onImported?(await Task.detached(priority: .utility) { try? store.count() }.value)
        }
        return result
    }
}
