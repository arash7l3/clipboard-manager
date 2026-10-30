import AppKit
@testable import Clipo
import ClipoCore
import Testing

@MainActor
struct PasteboardWatcherTests {
    private let board = TestPasteboard()

    private func watcher(
        app: SourceApp? = SourceApp(id: "com.example.editor", name: "Editor"),
        shouldRead: @escaping @MainActor (SourceApp?) -> Bool = { _ in true }
    ) -> PasteboardWatcher {
        PasteboardWatcher(
            pasteboard: board.pasteboard,
            frontmostApp: { app },
            shouldRead: shouldRead
        )
    }

    /// Stops the watcher and returns everything it yielded.
    private func drain(_ watcher: PasteboardWatcher) async -> [PasteboardChange] {
        watcher.stop()
        var result: [PasteboardChange] = []
        for await change in watcher.changes {
            result.append(change)
        }
        return result
    }

    @Test func yieldsEachChangeInOrderWithItsSourceApp() async {
        let watcher = watcher()
        watcher.start()

        board.set("first")
        watcher.poll()
        watcher.poll()
        board.set("second")
        watcher.poll()

        let changes = await drain(watcher)
        #expect(changes.map(\.content) == [.text("first", rtf: nil, html: nil), .text("second", rtf: nil, html: nil)])
        #expect(changes.allSatisfy { $0.sourceApp?.id == "com.example.editor" })
    }

    @Test func whatIsOnThePasteboardAtStartIsNotCaptured() async {
        board.set("old")
        let watcher = watcher()
        watcher.start()
        watcher.poll()

        #expect(await drain(watcher).isEmpty)
    }

    @Test func droppedUnreadWhenNotAllowed() async {
        var asked: [String?] = []
        let watcher = watcher(shouldRead: { app in
            asked.append(app?.id)
            return false
        })
        watcher.start()

        board.set("secret")
        watcher.poll()

        #expect(await drain(watcher).isEmpty)
        #expect(asked == ["com.example.editor"])
    }

    @Test func concealedChangeIsDropped() async {
        let watcher = watcher()
        watcher.start()

        board.set("hunter2", extra: [NSPasteboard.PasteboardType("org.nspasteboard.ConcealedType"): Data("1".utf8)])
        watcher.poll()
        board.set("after")
        watcher.poll()

        let changes = await drain(watcher)
        #expect(changes.map(\.content) == [.text("after", rtf: nil, html: nil)])
    }

    @Test func nothingIsYieldedAfterStop() async {
        let watcher = watcher()
        watcher.start()
        watcher.stop()

        board.set("late")
        watcher.poll()

        #expect(await drain(watcher).isEmpty)
    }

    @Test func ownWriteSkippedByAcknowledging() async {
        let watcher = watcher()
        watcher.start()

        board.set("restored by Clipo")
        watcher.skipCurrent()
        watcher.poll()
        board.set("copied by the user")
        watcher.poll()

        let changes = await drain(watcher)
        #expect(changes.map(\.content) == [.text("copied by the user", rtf: nil, html: nil)])
    }
}
