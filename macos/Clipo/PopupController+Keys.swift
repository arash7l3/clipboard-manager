import AppKit
import ClipoCore

extension PopupController: NSSearchFieldDelegate {
    func controlTextDidChange(_ obj: Notification) {
        guard (obj.object as? NSSearchField) === searchField else { return }
        model.setSearchText(searchField.stringValue)
    }

    func controlTextDidEndEditing(_ obj: Notification) {
        // A row's rename field; the search field never stops editing for long.
        guard (obj.object as? NSSearchField) !== searchField else { return }
        renameFieldEnded()
    }

    /// Keys typed in the search field that drive the list instead of the
    /// text, and keys typed in a row's rename field.
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        guard control === searchField else {
            return handleRename(commandSelector, in: control)
        }
        return handle(commandSelector)
    }

    /// Runs the list command for a key binding selector. Returns whether it
    /// was one; other selectors are left to the text field.
    @discardableResult
    func handle(_ command: Selector) -> Bool {
        if let move = selectionMove(for: command) {
            apply(move)
            return true
        }
        switch command {
        case #selector(NSResponder.insertTab(_:)):
            disarmClear()
            model.moveCategory(by: 1)
        case #selector(NSResponder.insertBacktab(_:)):
            disarmClear()
            model.moveCategory(by: -1)
        case #selector(NSResponder.insertNewline(_:)):
            chooseSelected()
        case #selector(NSResponder.insertNewlineIgnoringFieldEditor(_:)):
            // Option+Return.
            chooseSelected(plain: true)
        case #selector(NSResponder.cancelOperation(_:)):
            dismiss()
        default:
            return false
        }
        return true
    }

    private func apply(_ move: SelectionMove) {
        switch move {
        case let .offset(rows): model.moveSelection(by: rows)
        case .first: model.select(0)
        case .last: model.select(model.rows.count - 1)
        }
    }

    private enum SelectionMove {
        case offset(Int)
        case first
        case last
    }

    private func selectionMove(for command: Selector) -> SelectionMove? {
        switch command {
        case #selector(NSResponder.moveUp(_:)):
            .offset(-1)
        case #selector(NSResponder.moveDown(_:)):
            .offset(1)
        case #selector(NSResponder.pageUp(_:)), #selector(NSResponder.scrollPageUp(_:)):
            .offset(-pageRows)
        case #selector(NSResponder.pageDown(_:)), #selector(NSResponder.scrollPageDown(_:)):
            .offset(pageRows)
        case #selector(NSResponder.moveToBeginningOfDocument(_:)),
             #selector(NSResponder.scrollToBeginningOfDocument(_:)):
            .first
        case #selector(NSResponder.moveToEndOfDocument(_:)),
             #selector(NSResponder.scrollToEndOfDocument(_:)):
            .last
        default:
            nil
        }
    }
}
