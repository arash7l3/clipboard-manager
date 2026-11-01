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
    /// SF Symbol of the badge on the menu-bar icon while private mode is on.
    static let privateSymbolName = "eye.slash"

    let menu = NSMenu()
    /// Called when the user picks "Try Again" after the history failed to open.
    var onRetry: (@MainActor () -> Void)?
    /// Called when the user picks "Show Clipboard History".
    var onShowHistory: (@MainActor () -> Void)?
    /// Called when the user picks "Settings…".
    var onShowSettings: (@MainActor () -> Void)?

    /// Called when the user picks "Clear History…"; nothing is deleted until
    /// they confirm.
    var onClearHistory: (@MainActor () -> Void)?

    /// Called with the snippet the user picked from the Snippets submenu.
    var onPasteSnippet: (@MainActor (Snippet) -> Void)?

    let statusLine = NSMenuItem()
    let snippetsItem = NSMenuItem()
    let privateModeItem = NSMenuItem()
    let clearHistoryItem = NSMenuItem()
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

        clearHistoryItem.title = String(
            localized: "Clear History…",
            comment: "Menu item that deletes every unpinned item, after asking"
        )
        clearHistoryItem.target = self
        clearHistoryItem.action = #selector(clearHistory)

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
            clearHistoryItem, .separator(), settingsItem, aboutItem, .separator(), quitItem
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
    /// glance, a crossed-out eye on the icon.
    func showPrivateMode(_ enabled: Bool) {
        isPrivate = enabled
        privateModeItem.state = enabled ? .on : .off
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
        button.image = Self.icon(isPrivate: isPrivate, label: label)
        button.contentTintColor = nil
        button.toolTip = isPrivate ? label : nil
        // Private mode only changes how the icon looks; it never takes it away.
        statusItem?.isVisible = true
    }

    /// The menu-bar icon: the clipboard, with a crossed-out eye in its
    /// bottom-right corner while private mode is on, so it still reads as
    /// Clipo. A template image either way, tinted by the menu bar like the
    /// icons around it. Never `nil`, or the item would be empty and vanish
    /// from the menu bar.
    static func icon(isPrivate: Bool, label: String) -> NSImage {
        let clipboard = NSImage(systemSymbolName: symbolName, accessibilityDescription: label)
            ?? NSImage(size: NSSize(width: 18, height: 18))
        clipboard.isTemplate = true
        guard isPrivate, NSImage(systemSymbolName: privateSymbolName, accessibilityDescription: nil) != nil else {
            return clipboard
        }
        // Names, not images, go into the drawing handler, which may run off the main actor.
        let clipboardName = symbolName
        let badgeName = privateSymbolName
        let badged = NSImage(size: clipboard.size, flipped: false) { @Sendable bounds in
            guard let base = NSImage(systemSymbolName: clipboardName, accessibilityDescription: nil),
                  let badge = NSImage(systemSymbolName: badgeName, accessibilityDescription: nil) else {
                return false
            }
            let width = bounds.width * 0.66
            let height = width * badge.size.height / max(badge.size.width, 1)
            let badgeRect = NSRect(x: bounds.maxX - width, y: bounds.minY, width: width, height: height)
            base.draw(in: bounds)
            // A gap around the eye keeps it apart from the clipboard's outline.
            NSGraphicsContext.current?.compositingOperation = .destinationOut
            NSBezierPath(roundedRect: badgeRect.insetBy(dx: -1.5, dy: -1.5), xRadius: 3, yRadius: 3).fill()
            NSGraphicsContext.current?.compositingOperation = .sourceOver
            badge.draw(in: badgeRect)
            return true
        }
        badged.accessibilityDescription = label
        badged.isTemplate = true
        return badged
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

    @objc private func clearHistory() {
        onClearHistory?()
    }

    @objc private func showSettings() {
        onShowSettings?()
    }

    @objc private func showAbout() {
        // A menu-bar app is not active by default; bring the panel to the front.
        NSApp.activate()
        // Handed over explicitly: the panel's own lookup came up blank before.
        // AppDelegate sets this image from AppIcon.icns at launch.
        NSApp.orderFrontStandardAboutPanel(options: [.applicationIcon: NSApp.applicationIconImage as Any])
    }
}

extension StatusMenu: NSMenuItemValidation {
    /// "Show Clipboard History" and "Clear History…" are available only while
    /// the history is open.
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        (menuItem !== showHistoryItem && menuItem !== clearHistoryItem) || isReady
    }
}
