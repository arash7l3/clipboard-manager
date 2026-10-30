@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// Row fixtures.
enum Rows {
    static let copiedAt = Date(timeIntervalSince1970: 1_000_000)

    static func text(
        _ id: Int64,
        _ snippet: String,
        pinned: Bool = false,
        hint: TextHint = .plain,
        app: SourceApp? = nil
    ) -> ItemPreview {
        ItemPreview(
            id: id,
            content: .text(snippet: snippet, truncated: false, hint: hint),
            pinned: pinned,
            sourceApp: app,
            lastCopiedAt: copiedAt
        )
    }
}

/// A loader that answers from a closure and remembers what it was asked.
/// Called off the main thread, so it locks.
final class FakeLoader: @unchecked Sendable {
    private let lock = NSLock()
    private var asked: [Query] = []
    private var respond: @Sendable (Query) -> [ItemPreview]?

    init(_ respond: @escaping @Sendable (Query) -> [ItemPreview]?) {
        self.respond = respond
    }

    convenience init(rows: [ItemPreview]) {
        self.init { _ in rows }
    }

    var queries: [Query] {
        lock.withLock { asked }
    }

    /// Answers later loads with `respond`.
    func answer(with respond: @escaping @Sendable (Query) -> [ItemPreview]?) {
        lock.withLock { self.respond = respond }
    }

    func load(_ query: Query) -> [ItemPreview]? {
        let respond = lock.withLock {
            asked.append(query)
            return self.respond
        }
        return respond(query)
    }

    @MainActor
    func model() -> PopupModel {
        PopupModel(searchDelay: .zero) { [self] query in load(query) }
    }
}

@MainActor
struct PopupModelTests {
    private let three = [Rows.text(3, "c"), Rows.text(2, "b"), Rows.text(1, "a")]

    @Test func resetListsTheAllTabAndSelectsTheNewest() async {
        let loader = FakeLoader(rows: three)
        let model = loader.model()

        await model.reset().value

        #expect(model.rows.map(\.id) == [3, 2, 1])
        #expect(model.selection == 0)
        #expect(model.selectedRow?.id == 3)
        #expect(!model.isLoading)
        #expect(loader.queries == [Query(category: .all, text: "")])
    }

    @Test func resetDropsOldRowsAtOnce() async {
        let model = FakeLoader(rows: three).model()
        await model.reset().value

        let reload = model.reset()
        #expect(model.rows.isEmpty)
        #expect(model.selection == nil)
        #expect(model.isLoading)

        await reload.value
        #expect(model.rows.count == 3)
    }

    @Test func selectionStopsAtTheEnds() async {
        let model = FakeLoader(rows: three).model()
        await model.reset().value

        #expect(!model.moveSelection(by: -1))
        #expect(model.selection == 0)
        #expect(model.moveSelection(by: 1))
        #expect(model.moveSelection(by: 10))
        #expect(model.selection == 2)
        #expect(!model.moveSelection(by: 1))
        #expect(!model.select(7))
        #expect(model.select(0))
        #expect(model.selection == 0)
    }

    @Test func selectionMovesAreReported() async {
        let model = FakeLoader(rows: three).model()
        await model.reset().value
        var moves = 0
        model.onSelectionChange = { moves += 1 }

        model.moveSelection(by: 1)
        model.moveSelection(by: 0)
        model.select(1)

        #expect(moves == 1)
    }

    @Test func searchAsksForTheTypedTextInTheCurrentTab() async {
        let loader = FakeLoader { query in [Rows.text(1, query.text)] }
        let model = loader.model()
        await model.setCategory(.text).value

        await model.setSearchText("کتاب").value

        #expect(loader.queries.last == Query(category: .text, text: "کتاب"))
        #expect(model.rows.map(\.content) == [.text(snippet: "کتاب", truncated: false, hint: .plain)])
    }

    @Test func onlyTheLatestSearchIsShown() async {
        let loader = FakeLoader { query in [Rows.text(1, query.text)] }
        let model = loader.model()

        model.setSearchText("a")
        model.setSearchText("ab")
        await model.setSearchText("abc").value

        #expect(model.searchText == "abc")
        #expect(model.rows.map(\.content) == [.text(snippet: "abc", truncated: false, hint: .plain)])
    }

    @Test func refreshKeepsTheSelectedItem() async {
        let loader = FakeLoader(rows: three)
        let model = loader.model()
        await model.reset().value
        model.select(2)

        let newer = [Rows.text(4, "d")] + three
        loader.answer { _ in newer }
        await model.refresh().value

        #expect(model.rows.count == 4)
        #expect(model.selectedRow?.id == 1)
    }

    @Test func refreshFallsBackToTheFirstRow() async {
        let loader = FakeLoader(rows: three)
        let model = loader.model()
        await model.reset().value
        model.select(2)

        loader.answer { _ in [Rows.text(9, "z")] }
        await model.refresh().value

        #expect(model.selection == 0)
    }

    @Test func aFailedLoadShowsNoRows() async {
        let loader = FakeLoader(rows: three)
        let model = loader.model()
        await model.reset().value

        loader.answer { _ in nil }
        await model.refresh().value

        #expect(model.failed)
        #expect(model.rows.isEmpty)
        #expect(model.selection == nil)
        #expect(model.selectedRow == nil)
        #expect(!model.moveSelection(by: 1))
    }

    @Test func tabsWrapAround() async {
        let model = FakeLoader(rows: []).model()
        #expect(model.categories == [.all, .text, .images, .files, .pinned])

        await model.moveCategory(by: -1).value
        #expect(model.category == .pinned)
        await model.moveCategory(by: 1).value
        #expect(model.category == .all)
        await model.moveCategory(by: 2).value
        #expect(model.category == .images)
    }

    @Test func changingTabsSelectsTheFirstRow() async {
        let model = FakeLoader(rows: three).model()
        await model.reset().value
        model.select(2)

        await model.setCategory(.pinned).value

        #expect(model.selection == 0)
    }
}
