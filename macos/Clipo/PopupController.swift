import AppKit
import ClipoCore

/// The history popup: a search field, category tabs and the list of items.
///
/// The search field keeps the keyboard the whole time. Typing searches;
/// Up, Down, Page Up, Page Down, Home and End move the selection; Tab and
/// Shift+Tab switch tabs; Enter picks the selected item; Esc closes. A click
/// on a row picks it, and a click outside the popup closes it.
@MainActor
final class PopupController: NSViewController {
    let model: PopupModel

    /// Called with the item the user picked, after the popup has closed.
    var onChoose: (@MainActor (ItemPreview) -> Void)?
    /// The current time, for "copied 5 min. ago".
    var now: () -> Date = Date.init

    let searchField = NSSearchField()
    let tabs = NSSegmentedControl()
    let table = NSTableView()
    let emptyLabel = NSTextField(labelWithString: "")
    private let scrollView = NSScrollView()
    private var isDismissing = false
    private lazy var panel: PopupPanel = {
        let panel = PopupPanel(content: view)
        panel.onDismiss = { [weak self] in self?.dismiss() }
        return panel
    }()

    init(model: PopupModel) {
        self.model = model
        super.init(nibName: nil, bundle: nil)
        model.onRowsChange = { [weak self] in self?.rowsChanged() }
        model.onSelectionChange = { [weak self] in self?.selectionMoved() }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PopupController is built in code")
    }

    var isShown: Bool {
        isViewLoaded && panel.isVisible
    }

    // MARK: Showing

    /// Opens the popup at the mouse pointer on the All tab with an empty
    /// search, or closes it if it is open.
    func toggle() {
        if isShown {
            dismiss()
        } else {
            show(at: NSEvent.mouseLocation)
        }
    }

    func show(at cursor: NSPoint) {
        loadViewIfNeeded()
        searchField.stringValue = ""
        model.reset()
        if let visible = PopupPlacement.visibleFrame(around: cursor) {
            let frame = PopupPlacement.frame(
                size: panel.frame.size,
                cursor: cursor,
                visible: visible,
                layoutDirection: NSApp.userInterfaceLayoutDirection
            )
            panel.setFrame(frame, display: false)
        }
        // Key without activating Clipo: the app underneath stays frontmost.
        panel.orderFrontRegardless()
        panel.makeKey()
        _ = panel.makeFirstResponder(searchField)
    }

    func dismiss() {
        // Ordering out makes the panel resign key, which asks to dismiss again.
        guard isShown, !isDismissing else { return }
        isDismissing = true
        defer { isDismissing = false }
        panel.orderOut(nil)
    }

    /// Lists new copies while the popup is open.
    func historyChanged() {
        if isShown {
            model.refresh()
        }
    }

    // MARK: View

    override func loadView() {
        configureSearchField()
        configureTabs()
        configureTable()
        emptyLabel.textColor = .secondaryLabelColor
        emptyLabel.alignment = .center
        emptyLabel.isHidden = true
        view = layOut()
    }

    private func configureSearchField() {
        searchField.placeholderString = String(localized: "Search", comment: "Placeholder of the popup's search field")
        searchField.setAccessibilityLabel(String(
            localized: "Search clipboard history",
            comment: "Screen-reader label of the popup's search field"
        ))
        searchField.sendsSearchStringImmediately = true
        searchField.target = self
        searchField.action = #selector(searchChanged(_:))
        searchField.delegate = self
    }

    private func configureTabs() {
        tabs.segmentCount = model.categories.count
        for (index, category) in model.categories.enumerated() {
            tabs.setLabel(PopupText.tabTitle(category), forSegment: index)
        }
        tabs.trackingMode = .selectOne
        tabs.segmentDistribution = .fillEqually
        tabs.selectedSegment = 0
        tabs.target = self
        tabs.action = #selector(tabChosen(_:))
        // Keys stay in the search field; Tab switches tabs from there.
        tabs.refusesFirstResponder = true
    }

    private func configureTable() {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("item"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.headerView = nil
        table.style = .inset
        table.rowHeight = HistoryRowView.height
        table.backgroundColor = .clear
        table.allowsMultipleSelection = false
        table.allowsEmptySelection = true
        table.refusesFirstResponder = true
        table.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.action = #selector(rowClicked(_:))
        table.setAccessibilityLabel(String(
            localized: "Clipboard items",
            comment: "Screen-reader label of the popup's list of items"
        ))

        scrollView.documentView = table
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.drawsBackground = false
    }

    /// Search field, then tabs, then the list filling the rest; leading and
    /// trailing edges so the layout mirrors in right-to-left languages.
    private func layOut() -> NSView {
        let root = NSView(frame: NSRect(origin: .zero, size: PopupPanel.defaultSize))
        for subview in [searchField, tabs, scrollView, emptyLabel] {
            subview.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(subview)
        }
        let margin: CGFloat = 12
        NSLayoutConstraint.activate([
            searchField.topAnchor.constraint(equalTo: root.topAnchor, constant: margin),
            searchField.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: margin),
            searchField.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -margin),
            tabs.topAnchor.constraint(equalTo: searchField.bottomAnchor, constant: 8),
            tabs.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: margin),
            tabs.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -margin),
            scrollView.topAnchor.constraint(equalTo: tabs.bottomAnchor, constant: 8),
            scrollView.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scrollView.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -6),
            emptyLabel.centerXAnchor.constraint(equalTo: scrollView.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scrollView.centerYAnchor),
            emptyLabel.leadingAnchor.constraint(greaterThanOrEqualTo: root.leadingAnchor, constant: margin),
            emptyLabel.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -margin)
        ])
        return root
    }

    // MARK: Model to view

    private func rowsChanged() {
        guard isViewLoaded else { return }
        table.reloadData()
        if let index = model.categories.firstIndex(of: model.category) {
            tabs.selectedSegment = index
        }
        emptyLabel.stringValue = PopupText.emptyList(failed: model.failed, searching: !model.searchText.isEmpty)
        emptyLabel.isHidden = !model.rows.isEmpty || model.isLoading
        showSelection()
    }

    private func selectionMoved() {
        guard isViewLoaded else { return }
        showSelection()
        // The search field keeps focus, so a screen reader would not notice
        // the selection moving; say the new row instead.
        if let row = model.selectedRow {
            NSAccessibility.post(
                element: table,
                notification: .announcementRequested,
                userInfo: [
                    .announcement: PopupText.accessibilityLabel(row, now: now()),
                    .priority: NSAccessibilityPriorityLevel.high.rawValue
                ]
            )
        }
    }

    private func showSelection() {
        guard let index = model.selection else {
            table.deselectAll(nil)
            return
        }
        table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        table.scrollRowToVisible(index)
    }

    // MARK: Actions

    /// Rows that fit in the list, less one, for Page Up and Page Down.
    private var pageRows: Int {
        max(1, Int(scrollView.contentView.bounds.height / table.rowHeight) - 1)
    }

    func chooseSelected() {
        guard let row = model.selectedRow else {
            NSSound.beep()
            return
        }
        dismiss()
        onChoose?(row)
    }

    @objc private func searchChanged(_ sender: NSSearchField) {
        model.setSearchText(sender.stringValue)
    }

    @objc private func tabChosen(_ sender: NSSegmentedControl) {
        let index = sender.selectedSegment
        guard model.categories.indices.contains(index) else { return }
        model.setCategory(model.categories[index])
        _ = view.window?.makeFirstResponder(searchField)
    }

    @objc private func rowClicked(_ sender: NSTableView) {
        let row = sender.clickedRow
        guard model.rows.indices.contains(row) else { return }
        model.select(row)
        chooseSelected()
    }
}

extension PopupController: NSSearchFieldDelegate {
    func controlTextDidChange(_ obj: Notification) {
        model.setSearchText(searchField.stringValue)
    }

    /// Keys typed in the search field that drive the list instead of the text.
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        handle(commandSelector)
    }

    /// Runs the list command for a key binding selector. Returns whether it
    /// was one; other selectors are left to the text field.
    @discardableResult
    func handle(_ command: Selector) -> Bool {
        if let move = selectionMove(for: command) {
            switch move {
            case let .offset(rows): model.moveSelection(by: rows)
            case .first: model.select(0)
            case .last: model.select(model.rows.count - 1)
            }
            return true
        }
        switch command {
        case #selector(NSResponder.insertTab(_:)):
            model.moveCategory(by: 1)
        case #selector(NSResponder.insertBacktab(_:)):
            model.moveCategory(by: -1)
        case #selector(NSResponder.insertNewline(_:)):
            chooseSelected()
        case #selector(NSResponder.cancelOperation(_:)):
            dismiss()
        default:
            return false
        }
        return true
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

extension PopupController: NSTableViewDataSource, NSTableViewDelegate {
    func numberOfRows(in tableView: NSTableView) -> Int {
        model.rows.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard model.rows.indices.contains(row) else { return nil }
        let cell = tableView.makeView(withIdentifier: HistoryRowView.reuseIdentifier, owner: self) as? HistoryRowView
            ?? HistoryRowView()
        cell.show(model.rows[row], now: now())
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        // Only mouse selection gets here with a new row; the model's own
        // changes come back as no-ops.
        let row = table.selectedRow
        if row >= 0 {
            model.select(row)
        }
    }
}
