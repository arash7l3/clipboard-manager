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
