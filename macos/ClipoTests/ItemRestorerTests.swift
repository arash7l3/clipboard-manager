import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// What the restorer did besides writing the pasteboard, on the main actor.
@MainActor
private final class Effects {
    var pastes = 0
    var writes = 0
}

/// Ids moved to the top, which happens on a background task, so it locks.
private final class Bumps: @unchecked Sendable {
    private let lock = NSLock()
    private var ids: [Int64] = []

    func add(_ id: Int64) {
        lock.withLock { ids.append(id) }
    }

    var all: [Int64] {
        lock.withLock { ids }
    }
}

@MainActor
struct ItemRestorerTests {
    private let board = TestPasteboard()
    private let effects = Effects()
    private let bumps = Bumps()

    private func item(_ id: Int64, _ text: String) -> ClipItem {
        ClipItem(
            id: id,
            content: .text(text: TextContent(plain: text, rich: nil)),
            pinned: false,
            sourceApp: nil,
            createdAt: Rows.copiedAt,
            lastCopiedAt: Rows.copiedAt
        )
    }

    private func restorer(
        items: [ClipItem],
        autoPaste: Bool = false,
        accessibility: Bool = true
    ) -> ItemRestorer {
        let restorer = ItemRestorer(pasteboard: board.pasteboard)
        restorer.load = { id in items.first { $0.id == id } }
        let bumps = bumps
        restorer.bump = { id in bumps.add(id) }
        restorer.didWrite = { [effects] in effects.writes += 1 }
        restorer.autoPasteEnabled = { autoPaste }
        restorer.hasAccessibility = { accessibility }
        restorer.sendPaste = { [effects] in effects.pastes += 1 }
        return restorer
    }

    @Test func pickedItemLandsOnThePasteboard() async {
        let restorer = restorer(items: [item(1, "one"), item(2, "two")])

        let outcome = await restorer.restore(Rows.text(2, "two"))

        #expect(outcome == .restored)
        #expect(board.pasteboard.string(forType: .string) == "two")
        #expect(effects.writes == 1)
        #expect(effects.pastes == 0)
    }

    @Test func restoredItemMovesToTheTop() async throws {
        let restorer = restorer(items: [item(7, "seven")])

        _ = await restorer.restore(Rows.text(7, "seven"))
        // The bump runs in the background.
        for _ in 0..<100 where bumps.all.isEmpty {
            try await Task.sleep(for: .milliseconds(10))
        }

        #expect(bumps.all == [7])
    }

    @Test func autoPastePastesWhenAllowed() async {
        let restorer = restorer(items: [item(1, "one")], autoPaste: true)

        let outcome = await restorer.restore(Rows.text(1, "one"))

        #expect(outcome == .pasted)
        #expect(board.pasteboard.string(forType: .string) == "one")
        #expect(effects.pastes == 1)
    }

    @Test func withoutPermissionTheItemIsRestoredButNotPasted() async {
        let restorer = restorer(items: [item(1, "one")], autoPaste: true, accessibility: false)

        let outcome = await restorer.restore(Rows.text(1, "one"))

        #expect(outcome == .needsAccessibility)
        #expect(board.pasteboard.string(forType: .string) == "one")
        #expect(effects.pastes == 0)
    }

    @Test func permissionIsNotNeededWhenAutoPasteIsOff() async {
        let restorer = restorer(items: [item(1, "one")], autoPaste: false, accessibility: false)

        #expect(await restorer.restore(Rows.text(1, "one")) == .restored)
    }

    @Test func missingItemLeavesThePasteboardAlone() async {
        board.set("untouched")
        let restorer = restorer(items: [], autoPaste: true)

        let outcome = await restorer.restore(Rows.text(9, "gone"))

        #expect(outcome == .unavailable)
        #expect(board.pasteboard.string(forType: .string) == "untouched")
        #expect(effects.writes == 0)
        #expect(effects.pastes == 0)
        #expect(bumps.all.isEmpty)
    }

    @Test func autoPasteIsOffByDefault() throws {
        let suite = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }

        #expect(!PasteSettings.autoPaste(defaults: defaults))
        defaults.set(true, forKey: PasteSettings.autoPasteKey)
        #expect(PasteSettings.autoPaste(defaults: defaults))
    }
}
