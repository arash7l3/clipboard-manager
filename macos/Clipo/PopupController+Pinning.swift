import AppKit
import ClipoCore

extension PopupController: NSMenuDelegate {
    /// A right-click selects the row under the pointer and offers to pin or
    /// unpin it; no menu for empty space.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let row = table.clickedRow
        guard model.rows.indices.contains(row) else { return }
        model.select(row)
        let title = model.rows[row].pinned
            ? String(localized: "Unpin", comment: "Context menu item that unpins a history item")
            : String(localized: "Pin", comment: "Context menu item that pins a history item")
        let item = NSMenuItem(title: title, action: #selector(pinMenuChosen(_:)), keyEquivalent: "p")
        item.keyEquivalentModifierMask = .command
        item.target = self
        menu.addItem(item)
    }

    @objc private func pinMenuChosen(_ sender: NSMenuItem) {
        togglePinOfSelected()
    }
}

extension PopupController {
    /// Pins the selected item, or unpins it if it is pinned.
    func togglePinOfSelected() {
        guard let row = model.selectedRow else {
            NSSound.beep()
            return
        }
        let pinned = !row.pinned
        Task {
            await onSetPinned?(row, pinned)
            model.refresh()
        }
    }
}
