import AppKit
import ClipoCore

extension PopupController {
    func connectEmoji() {
        emojiPicker.isHidden = true
        model.onTabChange = { [weak self] in self?.tabChanged() }
        emoji.onSectionsChange = { [weak self] in self?.emojiPicker.reload() }
        emoji.onSelectionChange = { [weak self] in self?.emojiSelectionMoved() }
        emojiPicker.onChoose = { [weak self] picked in self?.choose(picked) }
    }

    /// Shows the list or the emoji grid for the model's tab.
    func tabChanged() {
        guard isViewLoaded else { return }
        if let index = model.tabs.firstIndex(of: model.tab) {
            tabs.selectedSegment = index
        }
        let showsEmoji = model.isEmojiTab
        scrollView.isHidden = showsEmoji
        emojiPicker.isHidden = !showsEmoji
        if showsEmoji {
            emptyLabel.isHidden = true
        }
        showClearButton()
        guard showsEmoji else { return }
        emoji.setSearchText(model.searchText)
        emoji.reloadRecent()
        emojiPicker.reload()
    }

    /// The search field's text changed: it filters the list, or the emoji.
    func searchTextChanged(_ text: String) {
        model.setSearchText(text)
        if model.isEmojiTab {
            emoji.setSearchText(text)
        }
    }

    /// Runs the emoji grid command for a key binding selector. Returns whether
    /// it was one; other selectors go on to the list's commands.
    func handleEmoji(_ command: Selector) -> Bool {
        if let move = Self.emojiMoves[command] {
            emoji.move(move)
            return true
        }
        guard command == #selector(NSResponder.insertNewline(_:))
            || command == #selector(NSResponder.insertNewlineIgnoringFieldEditor(_:))
        else { return false }
        chooseSelectedEmoji()
        return true
    }

    /// Left and Right move through the grid even with text in the search
    /// field, where they would move the caret.
    private static let emojiMoves: [Selector: EmojiModel.Move] = [
        #selector(NSResponder.moveLeft(_:)): .step(-1),
        #selector(NSResponder.moveRight(_:)): .step(1),
        #selector(NSResponder.moveUp(_:)): .rows(-1),
        #selector(NSResponder.moveDown(_:)): .rows(1),
        #selector(NSResponder.pageUp(_:)): .pages(-1),
        #selector(NSResponder.scrollPageUp(_:)): .pages(-1),
        #selector(NSResponder.pageDown(_:)): .pages(1),
        #selector(NSResponder.scrollPageDown(_:)): .pages(1),
        #selector(NSResponder.moveToBeginningOfDocument(_:)): .first,
        #selector(NSResponder.scrollToBeginningOfDocument(_:)): .first,
        #selector(NSResponder.moveToEndOfDocument(_:)): .last,
        #selector(NSResponder.scrollToEndOfDocument(_:)): .last
    ]

    /// Picks the selected emoji.
    func chooseSelectedEmoji() {
        guard let picked = emoji.selectedEmoji else {
            NSSound.beep()
            return
        }
        choose(picked)
    }

    /// Closes the popup and hands over `picked`.
    func choose(_ picked: Emoji) {
        dismiss()
        onChooseEmoji?(picked)
    }

    /// The search field keeps focus, so a screen reader would not notice the
    /// selection moving; say the new emoji instead.
    func emojiSelectionMoved() {
        guard isViewLoaded else { return }
        emojiPicker.selectionChanged()
        if let picked = emoji.selectedEmoji {
            NSAccessibility.post(
                element: emojiPicker.collection,
                notification: .announcementRequested,
                userInfo: [
                    .announcement: picked.name,
                    .priority: NSAccessibilityPriorityLevel.high.rawValue
                ]
            )
        }
    }
}
