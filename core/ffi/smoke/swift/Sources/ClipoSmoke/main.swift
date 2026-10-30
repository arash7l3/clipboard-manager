// Opens a store through the generated Swift bindings, records, searches and
// checks the error mapping. Exits non-zero on the first failure.

import ClipoCore
import Foundation

/// In-memory stand-in for the Keychain.
final class MemoryKeyStore: KeyStore, @unchecked Sendable {
    private let lock = NSLock()
    private var key: Data?

    func load() throws -> Data? {
        lock.withLock { key }
    }

    func save(key: Data) throws {
        lock.withLock { self.key = key }
    }
}

/// A key store whose reads fail, as the Keychain does when it is locked.
final class FailingKeyStore: KeyStore {
    func load() throws -> Data? {
        throw KeyStoreError.Failed(message: "OSStatus -25308")
    }

    func save(key: Data) throws {}
}

func check(_ condition: Bool, _ what: String, line: UInt = #line) {
    if !condition {
        FileHandle.standardError.write(Data("FAILED (line \(line)): \(what)\n".utf8))
        exit(1)
    }
}

func text(_ plain: String) -> ItemContent {
    .text(text: TextContent(plain: plain))
}

func plain(_ item: ClipItem) -> String? {
    if case let .text(text) = item.content { return text.plain }
    return nil
}

let dir = FileManager.default.temporaryDirectory
    .appendingPathComponent("clipo-smoke-\(UUID().uuidString)")
defer { try? FileManager.default.removeItem(at: dir) }
let path = dir.appendingPathComponent("history.db").path

do {
    let keys = MemoryKeyStore()
    let store = try ClipStore.open(path: path, keyStore: keys)
    check(try keys.load()?.count == 32, "a 32-byte key was saved")

    let start = Date(timeIntervalSince1970: 1_700_000_000)
    guard case let .added(first) = try store.record(
        capture: Capture(content: text("سلام دنیا"), capturedAt: start)
    ) else {
        check(false, "first capture is added")
        exit(1)
    }
    check(first.createdAt == start, "timestamps round-trip as Date")

    let image = ImageContent(format: .png, width: 1, height: 1, data: Data([1, 2, 3]))
    _ = try store.record(capture: Capture(
        content: .image(image: image),
        sourceApp: SourceApp(id: "com.apple.Preview", name: "Preview"),
        capturedAt: start.addingTimeInterval(1)
    ))

    guard case let .bumped(again) = try store.record(
        capture: Capture(content: text("سلام دنیا"), capturedAt: start.addingTimeInterval(2))
    ) else {
        check(false, "repeated copy is bumped")
        exit(1)
    }
    check(again.id == first.id, "bump keeps the id")

    let all = try store.search(query: Query(category: .all))
    check(all.count == 2, "two items stored")
    check(plain(all[0]) == "سلام دنیا", "bumped item is first")

    let found = try store.search(query: Query(category: .text, text: "سلام"))
    check(found.map(\.id) == [first.id], "search finds the Persian text")
    check(try store.search(query: Query(category: .images)).count == 1, "images tab")

    let concealed = try store.record(
        capture: Capture(content: text("password"), capturedAt: start, concealed: true)
    )
    check(concealed == .skipped(reason: .concealed), "concealed capture is skipped")

    try store.setPinned(id: first.id, pinned: true)
    check(try store.get(id: first.id)?.pinned == true, "pin")

    do {
        try store.delete(id: 9_999)
        check(false, "deleting a missing item throws")
    } catch ClipoError.ItemNotFound(let id) {
        check(id == 9_999, "ItemNotFound carries the id")
    }

    check(tabCategories() == [.all, .text, .images, .files, .pinned], "tab order")
    check(defaultHistoryLimits().maxItems == 200, "default limit")

    do {
        _ = try ClipStore.open(path: path, keyStore: MemoryKeyStore())
        check(false, "opening with an empty key store throws")
    } catch ClipoError.KeyMissing {}

    do {
        _ = try ClipStore.open(path: path, keyStore: FailingKeyStore())
        check(false, "a failing key store throws")
    } catch ClipoError.KeyStore(let message) {
        check(message == "OSStatus -25308", "key store message is passed through")
    }

    let reopened = try ClipStore.open(path: path, keyStore: keys)
    check(try reopened.count() == 2, "history survives a reopen")
} catch {
    check(false, "unexpected error: \(error)")
}

print("Swift smoke test passed")
