import AppKit
import ClipoCore

/// Renaming and deleting single items with the buttons at the end of each
/// row. A rename is edited in place: the row's title becomes a text field,
/// Return saves, Esc keeps the old name. A blank name brings back the
/// content's own first line.
extension PopupController {
    /// Starts renaming the item with `id`: its row shows a field holding its name.
    func beginRename(_ id: Int64) {
        if renamingID != nil {
            finishRename(saving: nil)
        }
        guard let index = model.rows.firstIndex(where: { $0.id == id }) else { return }
        renamingID = id
        model.select(index)
        table.reloadData(forRowIndexes: IndexSet(integer: index), columnIndexes: IndexSet(integer: 0))
        guard let cell = table.view(atColumn: 0, row: index, makeIfNecessary: true) as? HistoryRowView else {
            renamingID = nil
            return
        }
        _ = view.window?.makeFirstResponder(cell.editor)
    }

    /// Ends renaming: saves `title` as the item's name, or keeps the old
    /// name when it is `nil` or unchanged. The keyboard goes back to search.
    func finishRename(saving title: String?) {
        guard let id = renamingID else { return }
        renamingID = nil
        _ = view.window?.makeFirstResponder(searchField)
        let trimmed = title?.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let trimmed,
              let row = model.rows.first(where: { $0.id == id }),
              trimmed != PopupText.title(row) else {
            reloadRow(id)
            return
        }
        Task {
            await onRename?(row, trimmed)
            model.refresh()
        }
    }

    /// Ends renaming without saving, before the rows are listed again.
    func cancelRenameForReload() {
        guard renamingID != nil else { return }
        renamingID = nil
        _ = view.window?.makeFirstResponder(searchField)
    }

    /// Keys typed in the rename field. Returns whether the key was used.
    func handleRename(_ command: Selector, in field: NSControl) -> Bool {
        switch command {
        case #selector(NSResponder.insertNewline(_:)):
            finishRename(saving: field.stringValue)
        case #selector(NSResponder.cancelOperation(_:)):
            finishRename(saving: nil)
        case #selector(NSResponder.insertTab(_:)), #selector(NSResponder.insertBacktab(_:)):
            // The field keeps the keyboard; Tab switches tabs only from search.
            break
        default:
            return false
        }
        return true
    }

    /// The rename field lost the keyboard some other way, such as a click
    /// in the search field: the old name stays.
    func renameFieldEnded() {
        guard let id = renamingID else { return }
        renamingID = nil
        reloadRow(id)
    }

    /// Deletes the item with `id`. The popup stays open and lists the rows again.
    func deleteItem(_ id: Int64) {
        guard let row = model.rows.first(where: { $0.id == id }) else { return }
        if renamingID != nil {
            finishRename(saving: nil)
        }
        Task {
            await onDelete?(row)
            model.refresh()
        }
    }

    /// Draws the row of item `id` again, once the editing that is ending
    /// has let go of its field.
    private func reloadRow(_ id: Int64) {
        Task {
            guard renamingID != id, let index = model.rows.firstIndex(where: { $0.id == id }) else { return }
            table.reloadData(forRowIndexes: IndexSet(integer: index), columnIndexes: IndexSet(integer: 0))
        }
    }
}
