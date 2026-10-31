import ClipoCore
import Foundation
import os

/// Opens the encrypted history kept by the core.
///
/// Every call does disk and keychain I/O; the `Default` variants run off the
/// main thread.
enum History {
    /// What opening the history came to.
    enum Opened: Sendable {
        /// Open, holding `itemCount` items.
        case ready(ClipStore, itemCount: UInt64)
        /// The database exists but its key is missing from the key store or
        /// does not open it. Only starting over helps, so ask the user first.
        case keyProblem
        /// Anything else, such as a locked keychain or a disk error. Worth retrying.
        case failed
    }

    /// The key store for the user's history.
    static let keychain = KeychainKeyStore(service: "io.github.arash7l3.Clipo")

    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "history")

    /// `Clipo/history.db` inside `applicationSupport` (the user's
    /// Application Support folder by default). Creates the `Clipo` folder,
    /// readable by the user only.
    static func databaseURL(in applicationSupport: URL? = nil) throws -> URL {
        let support = try applicationSupport ?? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let folder = support.appendingPathComponent("Clipo", isDirectory: true)
        try FileManager.default.createDirectory(
            at: folder,
            withIntermediateDirectories: true,
            attributes: [.posixPermissions: 0o700]
        )
        return folder.appendingPathComponent("history.db", isDirectory: false)
    }

    /// Opens the database at `url` with its key in `keyStore`, creating both
    /// on first run.
    static func open(at url: URL, keyStore: any KeyStore) -> Opened {
        do {
            let store = try ClipStore.open(path: url.path, keyStore: keyStore)
            let itemCount = try store.count()
            return .ready(store, itemCount: itemCount)
        } catch ClipoError.KeyMissing, ClipoError.WrongKey, ClipoError.InvalidKey {
            logger.error("history key is missing or does not open the database")
            return .keyProblem
        } catch {
            // Core errors carry codes and ids, never clipboard content.
            logger.error("opening history failed: \(String(describing: error), privacy: .public)")
            return .failed
        }
    }

    /// Deletes the database at `url` and then its key, so the next
    /// ``open(at:keyStore:)`` starts an empty history with a new key. Close
    /// every store on `url` first. Returns whether both were deleted.
    static func startOver(at url: URL, keyStore: any ClearableKeyStore) -> Bool {
        do {
            // Database first: a key without a database is harmless, the
            // reverse would ask the user again.
            try deleteDatabase(path: url.path)
            try keyStore.delete()
            logger.notice("started a new history")
            return true
        } catch {
            logger.error("starting a new history failed: \(String(describing: error), privacy: .public)")
            return false
        }
    }

    /// What the popup lists for `query`, or `nil` if the store failed. Does
    /// disk I/O: call it off the main thread.
    static func previews(_ query: Query, in store: ClipStore) -> [ItemPreview]? {
        do {
            return try store.previews(query: query)
        } catch {
            // Core errors carry codes and ids, never clipboard content.
            logger.error("listing history failed: \(String(describing: error), privacy: .public)")
            return nil
        }
    }

    /// The full item with `id`, or `nil` if it is gone or the store failed.
    /// Does disk I/O: call it off the main thread.
    static func item(_ id: Int64, in store: ClipStore) -> ClipItem? {
        do {
            return try store.get(id: id)
        } catch {
            // Core errors carry codes and ids, never clipboard content.
            logger.error("loading an item failed: \(String(describing: error), privacy: .public)")
            return nil
        }
    }

    /// The plain text of the item with `id`, or `nil` for an image, a missing
    /// item or a store failure. Does disk I/O: call it off the main thread.
    static func plainText(_ id: Int64, in store: ClipStore) -> String? {
        do {
            return try store.plainText(id: id)
        } catch {
            logger.error("loading plain text failed: \(String(describing: error), privacy: .public)")
            return nil
        }
    }

    /// Moves the item with `id` to the top, as if it was copied again. Does
    /// disk I/O: call it off the main thread.
    static func bump(_ id: Int64, in store: ClipStore) {
        do {
            try store.touch(id: id, at: Date())
        } catch {
            logger.error("moving an item to the top failed: \(String(describing: error), privacy: .public)")
        }
    }

    /// Pins or unpins the item with `id`, off the main thread. Returns
    /// whether it worked. Unpinning may evict items past the history limit.
    static func setPinned(_ id: Int64, _ pinned: Bool, in store: ClipStore) async -> Bool {
        await Task.detached(priority: .userInitiated) {
            do {
                try store.setPinned(id: id, pinned: pinned)
                return true
            } catch {
                logger.error("pinning an item failed: \(String(describing: error), privacy: .public)")
                return false
            }
        }.value
    }

    /// Clears `category`'s tab, off the main thread: deletes its unpinned
    /// items. Returns how many items the history holds afterwards, or `nil`
    /// if clearing failed.
    static func clear(_ category: ClipoCore.Category, in store: ClipStore) async -> UInt64? {
        await Task.detached(priority: .userInitiated) {
            do {
                _ = try store.clear(category: category)
                return try store.count()
            } catch {
                logger.error("clearing a tab failed: \(String(describing: error), privacy: .public)")
                return nil
            }
        }.value
    }

    /// ``open(at:keyStore:)`` on the user's history, off the main thread.
    static func openDefault() async -> Opened {
        await Task.detached(priority: .userInitiated) {
            do {
                let url = try databaseURL()
                return open(at: url, keyStore: keychain)
            } catch {
                logger.error("no history folder: \(String(describing: error), privacy: .public)")
                return .failed
            }
        }.value
    }

    /// ``startOver(at:keyStore:)`` on the user's history, off the main thread.
    static func startOverDefault() async -> Bool {
        await Task.detached(priority: .userInitiated) {
            do {
                let url = try databaseURL()
                return startOver(at: url, keyStore: keychain)
            } catch {
                logger.error("no history folder: \(String(describing: error), privacy: .public)")
                return false
            }
        }.value
    }
}
