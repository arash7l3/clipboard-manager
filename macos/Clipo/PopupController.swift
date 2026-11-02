import AppKit
import ClipoCore

/// The history popup: a search field, category tabs and the list of items,
/// or, on the Emoji tab, a grid of emoji.
///
/// The search field keeps the keyboard the whole time. Typing searches;
/// Up, Down, Page Up, Page Down, Home and End move the selection (on the
/// Emoji tab Left and Right too); Tab and
/// Shift+Tab switch tabs; Enter picks the selected item; Esc closes. Cmd+P,
/// or Pin in a row's context menu, pins or unpins an item. The button beside
/// the tabs clears the tab after a second click. The buttons at the end of a
/// row rename and delete its item. A click on a row picks it, and a click
/// outside the popup closes it.
@MainActor
final class PopupController: NSViewController {
    let model: PopupModel
    let emoji: EmojiModel
    let emojiPicker: EmojiPickerView

    /// Called with the item the user picked, after the popup has closed.
    var onChoose: (@MainActor (ItemPreview) -> Void)?
    /// Called instead of ``onChoose`` when the user picked with Option held:
    /// paste only the item's text, without formatting.
    var onChoosePlain: (@MainActor (ItemPreview) -> Void)?
    /// Called with the item and whether it should now be pinned. The popup
    /// stays open and lists the rows again once this returns.
    var onSetPinned: (@MainActor (ItemPreview, Bool) async -> Void)?
    /// Called with the tab to clear after the user clicked Clear twice. The
    /// popup stays open and lists the rows again once this returns.
    var onClear: (@MainActor (ClipoCore.Category) async -> Void)?
    /// Called with the item and its new name; a blank name takes the name
    /// away. The popup stays open and lists the rows again once this returns.
    var onRename: (@MainActor (ItemPreview, String) async -> Void)?
    /// Called with the item to delete. The popup stays open and lists the
    /// rows again once this returns.
    var onDelete: (@MainActor (ItemPreview) async -> Void)?
    /// Called with the emoji the user picked, after the popup has closed.
    var onChooseEmoji: (@MainActor (Emoji) -> Void)?
    /// The current time, for "copied 5 min. ago".
    var now: () -> Date = Date.init

    let searchField = NSSearchField()
    let tabs = NSSegmentedControl()
    let clearButton = NSButton(title: "", target: nil, action: nil)
    var clearConfirmation = ClearConfirmation()
    var clearTimeout: Task<Void, Never>?
    /// The item whose row is being renamed, if any.
    var renamingID: Int64?
    let table = NSTableView()
    let emptyLabel = NSTextField(labelWithString: "")
    let scrollView = NSScrollView()
    let privateNote = NSTextField(labelWithString: "")
    private var scrollTopPlain: NSLayoutConstraint?
    private var scrollTopBelowNote: NSLayoutConstraint?
    private var isDismissing = false
    private lazy var panel: PopupPanel = {
        let panel = PopupPanel(content: view)
        panel.onDismiss = { [weak self] in self?.dismiss() }
        panel.onQuickPaste = { [weak self] index in self?.choose(rowAt: index) }
        panel.onTogglePin = { [weak self] in self?.togglePinOfSelected() }
        return panel
    }()

    init(model: PopupModel, emoji: EmojiModel = EmojiModel()) {
        self.model = model
        self.emoji = emoji
        emojiPicker = EmojiPickerView(model: emoji)
        super.init(nibName: nil, bundle: nil)
        model.onRowsChange = { [weak self] in self?.rowsChanged() }
        model.onSelectionChange = { [weak self] in self?.selectionMoved() }
        connectEmoji()
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
        disarmClear()
        model.reset(to: PopupSettings.startTab().tab(lastUsed: model.tab))
        showPrivateNote(UserDefaults.standard.bool(forKey: CaptureSettings.privateModeKey))
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
        disarmClear()
        renamingID = nil
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
        configureClearButton()
        configureTable()
        emptyLabel.textColor = .secondaryLabelColor
        emptyLabel.alignment = .center
        emptyLabel.isHidden = true
        configurePrivateNote()
        view = layOut()
    }

    /// Shows or hides the note about private mode and moves the list under it.
    func showPrivateNote(_ visible: Bool) {
        privateNote.isHidden = !visible
        scrollTopPlain?.isActive = !visible
        scrollTopBelowNote?.isActive = visible
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
        let contextMenu = NSMenu()
        contextMenu.delegate = self
        table.menu = contextMenu
        table.setAccessibilityLabel(String(
            localized: "Clipboard items",
            comment: "Screen-reader label of the popup's list of items"
        ))

        scrollView.documentView = table
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.drawsBackground = false
    }

    /// Search field, then tabs with the Clear button, then the list filling the rest; leading and
    /// trailing edges so the layout mirrors in right-to-left languages.
    private func layOut() -> NSView {
        let root = NSView(frame: NSRect(origin: .zero, size: PopupPanel.defaultSize))
        for subview in [searchField, tabs, clearButton, privateNote, scrollView, emojiPicker, emptyLabel] {
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
            tabs.trailingAnchor.constraint(equalTo: clearButton.leadingAnchor, constant: -8),
            clearButton.centerYAnchor.constraint(equalTo: tabs.centerYAnchor),
            clearButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -margin),
            privateNote.topAnchor.constraint(equalTo: tabs.bottomAnchor, constant: 6),
            privateNote.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: margin),
            privateNote.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -margin),
            scrollView.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scrollView.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -6),
            emojiPicker.topAnchor.constraint(equalTo: scrollView.topAnchor),
            emojiPicker.leadingAnchor.constraint(equalTo: scrollView.leadingAnchor),
            emojiPicker.trailingAnchor.constraint(equalTo: scrollView.trailingAnchor),
            emojiPicker.bottomAnchor.constraint(equalTo: scrollView.bottomAnchor),
            emptyLabel.centerXAnchor.constraint(equalTo: scrollView.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scrollView.centerYAnchor),
            emptyLabel.leadingAnchor.constraint(greaterThanOrEqualTo: root.leadingAnchor, constant: margin),
            emptyLabel.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -margin)
        ])
        let plain = scrollView.topAnchor.constraint(equalTo: tabs.bottomAnchor, constant: 8)
        let belowNote = scrollView.topAnchor.constraint(equalTo: privateNote.bottomAnchor, constant: 6)
        plain.isActive = true
        scrollTopPlain = plain
        scrollTopBelowNote = belowNote
        return root
    }

    // MARK: Model to view

    private func rowsChanged() {
        guard isViewLoaded else { return }
        cancelRenameForReload()
        table.reloadData()
        showClearButton()
        emptyLabel.stringValue = PopupText.emptyList(failed: model.failed, searching: !model.searchText.isEmpty)
        emptyLabel.isHidden = !model.rows.isEmpty || model.isLoading || model.isEmojiTab
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
    var pageRows: Int {
        max(1, Int(scrollView.contentView.bounds.height / table.rowHeight) - 1)
    }

    @objc func searchChanged(_ sender: NSSearchField) {
        searchTextChanged(sender.stringValue)
    }

    @objc func tabChosen(_ sender: NSSegmentedControl) {
        let index = sender.selectedSegment
        guard model.tabs.indices.contains(index) else { return }
        disarmClear()
        model.setTab(model.tabs[index])
        _ = view.window?.makeFirstResponder(searchField)
    }

    @objc private func rowClicked(_ sender: NSTableView) {
        let row = sender.clickedRow
        guard model.rows.indices.contains(row) else { return }
        model.select(row)
        chooseSelected(plain: NSEvent.modifierFlags.contains(.option))
    }
}

extension PopupController {
    func chooseSelected(plain: Bool = false) {
        guard !model.isEmojiTab else {
            chooseSelectedEmoji()
            return
        }
        guard let row = model.selectedRow else {
            NSSound.beep()
            return
        }
        dismiss()
        if plain {
            onChoosePlain?(row)
        } else {
            onChoose?(row)
        }
    }

    /// Picks the row at `index`, for the Cmd+digit shortcuts.
    func choose(rowAt index: Int) {
        guard !model.isEmojiTab, model.rows.indices.contains(index) else {
            NSSound.beep()
            return
        }
        model.select(index)
        chooseSelected()
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
        let item = model.rows[row]
        cell.show(item, now: now(), quickPaste: row < 9 ? row + 1 : nil, renaming: item.id == renamingID)
        cell.onRename = { [weak self] in self?.beginRename(item.id) }
        cell.onDelete = { [weak self] in self?.deleteItem(item.id) }
        cell.editor.delegate = self
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
