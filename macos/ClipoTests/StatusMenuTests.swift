import AppKit
@testable import Clipo
import Testing

@MainActor
struct StatusMenuTests {
    @Test func menuHasStatusAboutAndQuit() {
        let menu = StatusMenu()
        #expect(menu.menu.items.first === menu.statusLine)
        #expect(menu.menu.items.contains(menu.aboutItem))
        #expect(menu.menu.items.last === menu.quitItem)
        #expect(menu.quitItem.keyEquivalent == "q")
        #expect(menu.quitItem.action == #selector(NSApplication.terminate(_:)))
        #expect(menu.statusLine.action == nil)
    }

    @Test func retryShowsOnlyWhenUnavailable() {
        let menu = StatusMenu()
        #expect(menu.retryItem.isHidden)

        menu.show(.unavailable)
        #expect(!menu.retryItem.isHidden)

        menu.show(.ready(itemCount: 3))
        #expect(menu.retryItem.isHidden)
        #expect(menu.statusLine.title.contains("3"))
    }

    @Test func retryCallsBack() throws {
        let menu = StatusMenu()
        menu.onRetry = { menu.show(.opening) }
        menu.show(.unavailable)
        let action = try #require(menu.retryItem.action)
        _ = NSApp.sendAction(action, to: menu.retryItem.target, from: menu.retryItem)
        #expect(menu.retryItem.isHidden)
    }

    @Test func menuBarSymbolExists() {
        #expect(NSImage(systemSymbolName: StatusMenu.symbolName, accessibilityDescription: nil) != nil)
    }
}
