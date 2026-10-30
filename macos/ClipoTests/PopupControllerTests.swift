import AppKit
@testable import Clipo
import ClipoCore
import Testing

/// The popup's view and keys, without putting a window on screen.
@MainActor
struct PopupControllerTests {
    private let rows = [Rows.text(3, "third"), Rows.text(2, "second", pinned: true), Rows.text(1, "first")]

    private func loaded(_ rows: [ItemPreview]) async -> PopupController {
        let controller = PopupController(model: FakeLoader(rows: rows).model())
        controller.now = { Rows.copiedAt }
        controller.loadViewIfNeeded()
        await controller.model.reset().value
        return controller
    }

    @Test func listsTheRowsAndSelectsTheFirst() async throws {
        let controller = await loaded(rows)

        #expect(controller.table.numberOfRows == 3)
        #expect(controller.table.selectedRow == 0)
        #expect(controller.emptyLabel.isHidden)
        #expect(controller.tabs.segmentCount == 5)
        #expect(controller.tabs.selectedSegment == 0)

        let cell = try #require(
            controller.tableView(controller.table, viewFor: nil, row: 1) as? HistoryRowView
        )
        #expect(cell.textField?.stringValue == "second")
        #expect(cell.accessibilityLabel()?.contains("second") == true)
    }

    @Test func arrowsMoveTheSelection() async {
        let controller = await loaded(rows)

        #expect(controller.handle(#selector(NSResponder.moveDown(_:))))
        #expect(controller.table.selectedRow == 1)
        controller.handle(#selector(NSResponder.moveToEndOfDocument(_:)))
        #expect(controller.table.selectedRow == 2)
        controller.handle(#selector(NSResponder.moveUp(_:)))
        #expect(controller.table.selectedRow == 1)
        controller.handle(#selector(NSResponder.moveToBeginningOfDocument(_:)))
        #expect(controller.table.selectedRow == 0)
    }

    @Test func enterPicksTheSelectedRow() async {
        let controller = await loaded(rows)
        var picked: [Int64] = []
        controller.onChoose = { picked.append($0.id) }

        controller.handle(#selector(NSResponder.moveDown(_:)))
        #expect(controller.handle(#selector(NSResponder.insertNewline(_:))))

        #expect(picked == [2])
    }

    @Test func tabKeysSwitchTabs() async {
        let controller = await loaded(rows)

        controller.handle(#selector(NSResponder.insertTab(_:)))
        #expect(controller.model.category == .text)
        controller.handle(#selector(NSResponder.insertBacktab(_:)))
        controller.handle(#selector(NSResponder.insertBacktab(_:)))
        #expect(controller.model.category == .pinned)
    }

    @Test func escapeIsHandledAndTypingIsNot() async {
        let controller = await loaded(rows)

        #expect(controller.handle(#selector(NSResponder.cancelOperation(_:))))
        #expect(!controller.handle(#selector(NSResponder.deleteBackward(_:))))
        #expect(!controller.handle(#selector(NSResponder.moveLeft(_:))))
    }

    @Test func emptyTabSaysSo() async {
        let controller = await loaded([])

        #expect(controller.table.numberOfRows == 0)
        #expect(!controller.emptyLabel.isHidden)
        #expect(controller.emptyLabel.stringValue == PopupText.emptyList(failed: false, searching: false))
    }

    @Test func notShownUntilAsked() async {
        let controller = await loaded(rows)
        #expect(!controller.isShown)
        controller.dismiss()
        #expect(!controller.isShown)
    }
}
