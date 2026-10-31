import AppKit
import ClipoCore

/// The menu-bar icon and its menu.
@MainActor
final class StatusMenu: NSObject {
    /// The state of the history, shown in the menu's first line.
    enum Status: Equatable {
        case opening
        case ready(itemCount: UInt64)
        case unavailable
    }

    /// SF Symbol for the menu-bar icon.
    static let symbolName = "list.clipboard"
    /// SF Symbol for the menu-bar icon while private mode is on.
    static let privateSymbolName = "eye.slash"

    let menu = NSMenu()
    /// Called when the user picks "Try Again" after the history failed to open.
    var onRetry: (@MainActor () -> Void)?
    /// Called when the user picks "Show Clipboard History".
    var onShowHistory: (@MainActor () -> Void)?
    /// Called when the user picks "Settings…".
    var onShowSettings: (@MainActor () -> Void)?

    /// Called with the snippet the user picked from the Snippets submenu.
    var onPasteSnippet: (@MainActor (Snippet) -> Void)?

    let statusLine = NSMenuItem()
    let snippetsItem = NSMenuItem()
    let privateModeItem = NSMenuItem()
    let snippetsMenu = NSMenu()
    let retryItem = NSMenuItem()
    let showHistoryItem = NSMenuItem()
    let settingsItem = NSMenuItem()
    let aboutItem = NSMenuItem()
    let quitItem = NSMenuItem()

    /// The history is open, so the popup has something to show.
    private(set) var isReady = false
    /// Private mode is on: the icon looks different and the menu item is checked.
    private(set) var isPrivate = false

    private var statusItem: NSStatusItem?

    override init() {
        super.init()
        // No action, so the menu shows it disabled.
        statusLine.title = ""

        retryItem.title = String(localized: "Try Again", comment: "Menu item that retries opening the history")
        retryItem.target = self
        retryItem.action = #selector(retry)

        showHistoryItem.title = String(
            localized: "Show Clipboard History",
            comment: "Menu item that opens the history popup"
        )
        showHistoryItem.target = self
        showHistoryItem.action = #selector(showHistory)

        snippetsItem.title = String(
            localized: "Snippets",
            comment: "Menu item with the list of saved snippets"
        )
        snippetsItem.submenu = snippetsMenu
        showSnippets([])

        privateModeItem.title = String(
            localized: "Private Mode",
            comment: "Menu item that turns private mode on or off: nothing copied is saved while it is on"
        )
        privateModeItem.target = self
        privateModeItem.action = #selector(togglePrivateMode)

        settingsItem.title = String(localized: "Settings…", comment: "Menu item that opens the Settings window")
        settingsItem.target = self
        settingsItem.action = #selector(showSettings)
        settingsItem.keyEquivalent = ","

        aboutItem.title = String(localized: "About Clipo", comment: "Menu item that shows the About panel")
        aboutItem.target = self
        aboutItem.action = #selector(showAbout)

        quitItem.title = String(localized: "Quit Clipo", comment: "Menu item that quits the app")
        quitItem.action = #selector(NSApplication.terminate(_:))
        quitItem.keyEquivalent = "q"

        menu.items = [
            statusLine, retryItem, showHistoryItem, snippetsItem, privateModeItem, .separator(),
            settingsItem, aboutItem, .separator(), quitItem
        ]
        show(.opening)
    }

    /// Adds the icon to the menu bar. Keep the returned item: the icon goes
    /// away when it is released.
    func install() -> NSStatusItem {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem = item
        item.menu = menu
        updateIcon()
        return item
    }

    /// Shows whether private mode is on: a checked menu item and, at a
    /// glance, a different orange icon.
    func showPrivateMode(_ on: Bool) {
        isPrivate = on
        privateModeItem.state = on ? .on : .off
        updateIcon()
    }

    private func updateIcon() {
        guard let button = statusItem?.button else { return }
        let label = isPrivate
            ? String(
                localized: "Clipo clipboard history, private mode is on",
                comment: "Screen-reader label and tooltip of the menu-bar icon while private mode is on"
            )
            : String(localized: "Clipo clipboard history", comment: "Screen-reader label of the menu-bar icon")
        let image = NSImage(
            systemSymbolName: isPrivate ? Self.privateSymbolName : Self.symbolName,
            accessibilityDescription: label
        )
        image?.isTemplate = true
        button.image = image
        button.contentTintColor = isPrivate ? .systemOrange : nil
        button.toolTip = isPrivate ? label : nil
    }

    func show(_ status: Status) {
        switch status {
        case .opening:
            statusLine.title = String(localized: "Opening history…", comment: "Menu status while the history opens")
        case let .ready(itemCount):
            statusLine.title = String(
                localized: "\(Int(clamping: itemCount)) items in history",
                comment: "Menu status: number of stored clipboard items"
            )
        case .unavailable:
            statusLine.title = String(
                localized: "History unavailable",
                comment: "Menu status when the history could not be opened"
            )
        }
        retryItem.isHidden = status != .unavailable
        if case .ready = status {
            isReady = true
        } else {
            isReady = false
        }
    }

    /// Shows `combo` next to "Show Clipboard History", or nothing when the
    /// global shortcut could not be registered.
    func showShortcut(_ combo: KeyCombo?) {
        showHistoryItem.keyEquivalent = combo?.character ?? ""
        showHistoryItem.keyEquivalentModifierMask = combo?.modifiers ?? []
    }

    private var snippets: [Snippet] = []

    /// Lists `snippets` in the Snippets submenu; picking one calls
    /// ``onPasteSnippet``.
    func showSnippets(_ snippets: [Snippet]) {
        self.snippets = snippets
        snippetsMenu.removeAllItems()
        if snippets.isEmpty {
            // No action, so the menu shows it disabled.
            let empty = NSMenuItem()
            empty.title = String(
                localized: "No snippets. Add them in Settings.",
                comment: "Snippets submenu text when no snippet is saved"
            )
            snippetsMenu.addItem(empty)
            return
        }
        for (index, snippet) in snippets.enumerated() {
            let item = NSMenuItem()
            item.title = snippet.title
            item.tag = index
            item.target = self
            item.action = #selector(snippetChosen(_:))
            snippetsMenu.addItem(item)
        }
    }

    @objc private func snippetChosen(_ sender: NSMenuItem) {
        guard snippets.indices.contains(sender.tag) else { return }
        onPasteSnippet?(snippets[sender.tag])
    }

    @objc private func togglePrivateMode() {
        let defaults = UserDefaults.standard
        let wanted = !defaults.bool(forKey: CaptureSettings.privateModeKey)
        // The settings coordinator sees the change and applies the new rules.
        defaults.set(wanted, forKey: CaptureSettings.privateModeKey)
        showPrivateMode(wanted)
    }

    @objc private func retry() {
        onRetry?()
    }

    @objc private func showHistory() {
        onShowHistory?()
    }

    @objc private func showSettings() {
        onShowSettings?()
    }

    @objc private func showAbout() {
        // A menu-bar app is not active by default; bring the panel to the front.
        NSApp.activate()
        NSApp.orderFrontStandardAboutPanel(nil)
    }
}

extension StatusMenu: NSMenuItemValidation {
    /// "Show Clipboard History" is available only while the history is open.
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        menuItem !== showHistoryItem || isReady
    }
}
