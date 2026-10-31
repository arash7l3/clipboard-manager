import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

struct PopupSettingsTests {
    private func freshDefaults() throws -> UserDefaults {
        let suite = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defaults.removePersistentDomain(forName: suite)
        return defaults
    }

    @Test func startTabDefaultsToAll() throws {
        #expect(PopupSettings.startTab(defaults: try freshDefaults()) == .all)
    }

    @Test func startTabIsReadBackAndUnknownValuesFallBack() throws {
        let defaults = try freshDefaults()
        defaults.set("lastUsed", forKey: PopupSettings.startTabKey)
        #expect(PopupSettings.startTab(defaults: defaults) == .lastUsed)

        defaults.set("sideways", forKey: PopupSettings.startTabKey)
        #expect(PopupSettings.startTab(defaults: defaults) == .all)
    }

    @Test func eachChoiceOpensItsTab() {
        #expect(StartTab.all.category(lastUsed: .files) == .all)
        #expect(StartTab.text.category(lastUsed: .files) == .text)
        #expect(StartTab.images.category(lastUsed: .all) == .images)
        #expect(StartTab.files.category(lastUsed: .all) == .files)
        #expect(StartTab.pinned.category(lastUsed: .all) == .pinned)
        #expect(StartTab.lastUsed.category(lastUsed: .images) == .images)
    }

    @Test func everyChoiceHasATitle() {
        for choice in StartTab.allCases {
            #expect(!choice.title.isEmpty)
        }
    }
}

@MainActor
struct PrivateModeIndicatorTests {
    @Test func menuItemAndIconFollowPrivateMode() {
        let menu = StatusMenu()
        #expect(!menu.isPrivate)
        #expect(menu.privateModeItem.state == .off)
        #expect(menu.menu.items.contains(menu.privateModeItem))

        menu.showPrivateMode(true)
        #expect(menu.isPrivate)
        #expect(menu.privateModeItem.state == .on)

        menu.showPrivateMode(false)
        #expect(!menu.isPrivate)
        #expect(menu.privateModeItem.state == .off)
    }
}
