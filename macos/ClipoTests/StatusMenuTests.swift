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

    @Test func showHistoryIsEnabledOnlyWhenReady() throws {
        let menu = StatusMenu()
        #expect(menu.menu.items.contains(menu.showHistoryItem))
        #expect(!menu.validateMenuItem(menu.showHistoryItem))
        #expect(menu.validateMenuItem(menu.aboutItem))

        menu.show(.ready(itemCount: 0))
        #expect(menu.validateMenuItem(menu.showHistoryItem))

        var shown = 0
        menu.onShowHistory = { shown += 1 }
        let action = try #require(menu.showHistoryItem.action)
        _ = NSApp.sendAction(action, to: menu.showHistoryItem.target, from: menu.showHistoryItem)
        #expect(shown == 1)

        menu.show(.unavailable)
        #expect(!menu.validateMenuItem(menu.showHistoryItem))
    }

    @Test func settingsItemCallsBack() throws {
        let menu = StatusMenu()
        #expect(menu.menu.items.contains(menu.settingsItem))
        #expect(menu.settingsItem.keyEquivalent == ",")
        #expect(menu.validateMenuItem(menu.settingsItem))

        var shown = 0
        menu.onShowSettings = { shown += 1 }
        let action = try #require(menu.settingsItem.action)
        _ = NSApp.sendAction(action, to: menu.settingsItem.target, from: menu.settingsItem)
        #expect(shown == 1)
    }

    @Test func shortcutShowsOnlyWhenRegistered() {
        let menu = StatusMenu()
        menu.showShortcut(.showHistory)
        #expect(menu.showHistoryItem.keyEquivalent == "v")
        #expect(menu.showHistoryItem.keyEquivalentModifierMask == [.command, .shift])

        menu.showShortcut(nil)
        #expect(menu.showHistoryItem.keyEquivalent.isEmpty)
    }

    @Test func menuBarSymbolExists() {
        #expect(NSImage(systemSymbolName: StatusMenu.symbolName, accessibilityDescription: nil) != nil)
    }
}
