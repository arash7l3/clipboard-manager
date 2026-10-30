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

/// Layout in right-to-left languages and what a screen reader finds.
@MainActor
struct PopupAccessibilityTests {
    private func row(_ direction: NSUserInterfaceLayoutDirection) -> HistoryRowView {
        let view = HistoryRowView()
        setDirection(direction, on: view)
        view.frame = NSRect(x: 0, y: 0, width: 320, height: HistoryRowView.height)
        view.show(Rows.text(1, "first", pinned: true), now: Rows.copiedAt)
        view.layoutSubtreeIfNeeded()
        return view
    }

    /// The direction is per view, not inherited; the app sets it on the whole
    /// window in a right-to-left language.
    private func setDirection(_ direction: NSUserInterfaceLayoutDirection, on view: NSView) {
        view.userInterfaceLayoutDirection = direction
        for subview in view.subviews {
            setDirection(direction, on: subview)
        }
    }

    /// A subview's frame in the row's own coordinates.
    private func frame(of subview: NSView, in row: HistoryRowView) -> NSRect {
        row.convert(subview.bounds, from: subview)
    }

    @Test func rowPutsTheIconAtTheLeadingEdge() {
        let ltr = row(.leftToRight)
        #expect(frame(of: ltr.icon, in: ltr).minX < frame(of: ltr.title, in: ltr).minX)

        let rtl = row(.rightToLeft)
        #expect(frame(of: rtl.icon, in: rtl).maxX > frame(of: rtl.title, in: rtl).maxX)
        #expect(frame(of: rtl.icon, in: rtl).maxX <= rtl.bounds.maxX)
    }

    @Test func rowIsOneElementForScreenReaders() {
        let view = row(.leftToRight)
        #expect(view.accessibilityChildren()?.isEmpty == true)
        #expect(view.accessibilityLabel()?.contains("pinned") == true)
    }

    @Test func popupControlsHaveLabels() {
        let controller = PopupController(model: FakeLoader(rows: []).model())
        controller.loadViewIfNeeded()

        #expect(controller.searchField.accessibilityLabel()?.isEmpty == false)
        #expect(controller.tabs.accessibilityLabel()?.isEmpty == false)
        #expect(controller.table.accessibilityLabel()?.isEmpty == false)
    }
}
