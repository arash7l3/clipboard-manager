@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// In-memory stand-in for the keychain.
final class MemoryKeyStore: ClearableKeyStore, @unchecked Sendable {
    private let lock = NSLock()
    private var key: Data?

    func load() throws -> Data? {
        lock.withLock { key }
    }

    func save(key: Data) throws {
        lock.withLock { self.key = key }
    }

    func delete() throws {
        lock.withLock { key = nil }
    }
}

/// A key store whose reads fail, as the keychain does when it is locked.
final class LockedKeyStore: ClearableKeyStore {
    func load() throws -> Data? {
        throw KeyStoreError.Failed(message: "OSStatus -25308")
    }

    func save(key: Data) throws {}

    func delete() throws {}
}

/// Opening and resetting the history through the core: these also prove the
/// app links the core library.
final class HistoryTests {
    private let folder: URL

    init() throws {
        folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-tests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    deinit {
        try? FileManager.default.removeItem(at: folder)
    }

    @Test func databaseLivesInAPrivateClipoFolder() throws {
        let url = try History.databaseURL(in: folder)
        #expect(url == folder.appendingPathComponent("Clipo/history.db"))

        let attributes = try FileManager.default.attributesOfItem(atPath: url.deletingLastPathComponent().path)
        #expect((attributes[.posixPermissions] as? Int) == 0o700)
        let values = try url.deletingLastPathComponent().resourceValues(forKeys: [.isExcludedFromBackupKey])
        #expect(values.isExcludedFromBackup == true)
    }

    @Test func opensRecordsAndReopens() throws {
        let url = try History.databaseURL(in: folder)
        let keys = MemoryKeyStore()

        guard case let .ready(store, itemCount) = History.open(at: url, keyStore: keys) else {
            Issue.record("a new history opens")
            return
        }
        #expect(itemCount == 0)
        #expect(try keys.load()?.count == 32)

        let capture = Capture(content: .text(text: TextContent(plain: "سلام")), capturedAt: Date())
        guard case .added = try store.record(capture: capture) else {
            Issue.record("the capture is added")
            return
        }

        guard case let .ready(_, reopenedCount) = History.open(at: url, keyStore: keys) else {
            Issue.record("the history reopens with the same key")
            return
        }
        #expect(reopenedCount == 1)
    }

    @Test func missingKeyAsksToStartOver() throws {
        let url = try History.databaseURL(in: folder)
        guard case .ready = History.open(at: url, keyStore: MemoryKeyStore()) else {
            Issue.record("a new history opens")
            return
        }

        let emptyKeys = MemoryKeyStore()
        guard case .keyProblem = History.open(at: url, keyStore: emptyKeys) else {
            Issue.record("a database without its key is a key problem")
            return
        }

        #expect(History.startOver(at: url, keyStore: emptyKeys))
        guard case let .ready(_, itemCount) = History.open(at: url, keyStore: emptyKeys) else {
            Issue.record("a new history opens after starting over")
            return
        }
        #expect(itemCount == 0)
    }

    @Test func startingOverReplacesAWrongKey() throws {
        let url = try History.databaseURL(in: folder)
        guard case .ready = History.open(at: url, keyStore: MemoryKeyStore()) else {
            Issue.record("a new history opens")
            return
        }

        let otherKeys = MemoryKeyStore()
        try otherKeys.save(key: Data(repeating: 7, count: 32))
        guard case .keyProblem = History.open(at: url, keyStore: otherKeys) else {
            Issue.record("a key that does not open the database is a key problem")
            return
        }

        #expect(History.startOver(at: url, keyStore: otherKeys))
        #expect(try otherKeys.load() == nil)
        guard case .ready = History.open(at: url, keyStore: otherKeys) else {
            Issue.record("a new history opens after starting over")
            return
        }
        #expect(try otherKeys.load() != Data(repeating: 7, count: 32))
    }

    @Test func lockedKeychainIsRetryableNotAKeyProblem() throws {
        let url = try History.databaseURL(in: folder)
        guard case .failed = History.open(at: url, keyStore: LockedKeyStore()) else {
            Issue.record("a key store failure is retryable")
            return
        }
        #expect(!FileManager.default.fileExists(atPath: url.path))
    }

    @Test func clearingKeepsPinnedItems() async throws {
        let url = try History.databaseURL(in: folder)
        guard case let .ready(store, _) = History.open(at: url, keyStore: MemoryKeyStore()) else {
            Issue.record("a new history opens")
            return
        }
        for word in ["keep", "drop"] {
            _ = try store.record(capture: Capture(content: .text(text: TextContent(plain: word)), capturedAt: Date()))
        }
        let keep = try #require(try store.search(query: Query(category: .all, text: "keep")).first)
        try store.setPinned(id: keep.id, pinned: true)

        #expect(await History.clear(.pinned, in: store) == 2)
        #expect(await History.clear(.all, in: store) == 1)
        #expect(try store.get(id: keep.id)?.pinned == true)
    }

    @Test func defaultHistorySizeMatchesDecision0004() {
        #expect(defaultHistoryLimits().maxItems == 200)
    }
}
