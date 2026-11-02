import AppKit
import ClipoCore

extension PopupController {
    func configureSearchField() {
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

    func configureTabs() {
        tabs.segmentCount = model.tabs.count
        for (index, tab) in model.tabs.enumerated() {
            tabs.setLabel(PopupText.title(for: tab), forSegment: index)
        }
        tabs.trackingMode = .selectOne
        tabs.segmentDistribution = .fillEqually
        tabs.selectedSegment = 0
        tabs.target = self
        tabs.action = #selector(tabChosen(_:))
        // Keys stay in the search field; Tab switches tabs from there.
        tabs.refusesFirstResponder = true
        tabs.setAccessibilityLabel(String(
            localized: "Categories",
            comment: "Screen-reader label of the popup's tabs"
        ))
    }

    func configurePrivateNote() {
        privateNote.stringValue = String(
            localized: "Private mode is on. Nothing you copy is saved.",
            comment: "Note at the top of the popup while private mode is on"
        )
        privateNote.font = .systemFont(ofSize: 11)
        privateNote.textColor = .systemOrange
        privateNote.lineBreakMode = .byTruncatingTail
        privateNote.isHidden = true
    }
}
