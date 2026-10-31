import AppKit
@testable import Clipo
import ClipoCore
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

    @Test func clearHistoryIsEnabledOnlyWhenReadyAndCallsBack() throws {
        let menu = StatusMenu()
        #expect(menu.menu.items.contains(menu.clearHistoryItem))
        #expect(!menu.validateMenuItem(menu.clearHistoryItem))

        menu.show(.ready(itemCount: 1))
        #expect(menu.validateMenuItem(menu.clearHistoryItem))

        var asked = 0
        menu.onClearHistory = { asked += 1 }
        let action = try #require(menu.clearHistoryItem.action)
        _ = NSApp.sendAction(action, to: menu.clearHistoryItem.target, from: menu.clearHistoryItem)
        #expect(asked == 1)
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

    @Test func privateModeKeepsTheIconInTheMenuBar() {
        let menu = StatusMenu()
        let item = menu.install()
        menu.showPrivateMode(true)
        #expect(item.isVisible)
        #expect(item.button?.image != nil)
        menu.showPrivateMode(false)
        #expect(item.isVisible)
        #expect(item.button?.image != nil)
    }
}

@MainActor
struct SnippetsMenuTests {
    private func snippet(_ id: Int64, _ title: String) -> Snippet {
        Snippet(id: id, title: title, body: "body \(id)", createdAt: Rows.copiedAt, updatedAt: Rows.copiedAt)
    }

    @Test func emptySubmenuSaysSoAndDoesNothing() {
        let menu = StatusMenu()
        #expect(menu.menu.items.contains(menu.snippetsItem))
        #expect(menu.snippetsMenu.items.count == 1)
        #expect(menu.snippetsMenu.items.first?.action == nil)
    }

    @Test func pickingASnippetCallsBackWithIt() throws {
        let menu = StatusMenu()
        menu.showSnippets([snippet(1, "Greeting"), snippet(2, "Address")])
        var pasted: [Int64] = []
        menu.onPasteSnippet = { pasted.append($0.id) }

        #expect(menu.snippetsMenu.items.map(\.title) == ["Greeting", "Address"])
        let item = menu.snippetsMenu.items[1]
        let action = try #require(item.action)
        _ = NSApp.sendAction(action, to: item.target, from: item)

        #expect(pasted == [2])
    }
}
