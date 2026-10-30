import AppKit

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

    let menu = NSMenu()
    /// Called when the user picks "Try Again" after the history failed to open.
    var onRetry: (@MainActor () -> Void)?

    let statusLine = NSMenuItem()
    let retryItem = NSMenuItem()
    let aboutItem = NSMenuItem()
    let quitItem = NSMenuItem()

    override init() {
        super.init()
        // No action, so the menu shows it disabled.
        statusLine.title = ""

        retryItem.title = String(localized: "Try Again", comment: "Menu item that retries opening the history")
        retryItem.target = self
        retryItem.action = #selector(retry)

        aboutItem.title = String(localized: "About Clipo", comment: "Menu item that shows the About panel")
        aboutItem.target = self
        aboutItem.action = #selector(showAbout)

        quitItem.title = String(localized: "Quit Clipo", comment: "Menu item that quits the app")
        quitItem.action = #selector(NSApplication.terminate(_:))
        quitItem.keyEquivalent = "q"

        menu.items = [statusLine, retryItem, .separator(), aboutItem, .separator(), quitItem]
        show(.opening)
    }

    /// Adds the icon to the menu bar. Keep the returned item: the icon goes
    /// away when it is released.
    func install() -> NSStatusItem {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = item.button {
            let image = NSImage(
                systemSymbolName: Self.symbolName,
                accessibilityDescription: String(
                    localized: "Clipo clipboard history",
                    comment: "Screen-reader label of the menu-bar icon"
                )
            )
            image?.isTemplate = true
            button.image = image
        }
        item.menu = menu
        return item
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
    }

    @objc private func retry() {
        onRetry?()
    }

    @objc private func showAbout() {
        // A menu-bar app is not active by default; bring the panel to the front.
        NSApp.activate()
        NSApp.orderFrontStandardAboutPanel(nil)
    }
}
