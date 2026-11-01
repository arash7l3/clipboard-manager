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
    var refreshes = 0
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
        restorer.loadPlainText = { id in
            guard case let .text(text)? = items.first(where: { $0.id == id })?.content else { return nil }
            return text.plain
        }
        let bumps = bumps
        restorer.bump = { id in bumps.add(id) }
        restorer.didWrite = { [effects] in effects.writes += 1 }
        restorer.autoPasteEnabled = { autoPaste }
        restorer.hasAccessibility = { accessibility }
        restorer.sendPaste = { [effects] in effects.pastes += 1 }
        restorer.refreshTarget = { [effects] in effects.refreshes += 1 }
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

    @Test func plainTextDropsTheFormatting() async {
        let rich = RichText(format: .html, data: "<b>bold</b>")
        let formatted = ClipItem(
            id: 4,
            content: .text(text: TextContent(plain: "bold", rich: rich)),
            pinned: false,
            sourceApp: nil,
            createdAt: Rows.copiedAt,
            lastCopiedAt: Rows.copiedAt
        )
        let restorer = restorer(items: [formatted])

        let outcome = await restorer.restore(Rows.text(4, "bold"), plainText: true)

        #expect(outcome == .restored)
        #expect(board.pasteboard.string(forType: .string) == "bold")
        #expect(board.pasteboard.data(forType: .html) == nil)
    }

    @Test func plainTextOfAnImageIsUnavailable() async {
        board.set("untouched")
        let restorer = restorer(items: [])

        let outcome = await restorer.restore(Rows.text(5, "image"), plainText: true)

        #expect(outcome == .unavailable)
        #expect(board.pasteboard.string(forType: .string) == "untouched")
    }

    @Test func snippetTextIsPastedWithoutTouchingTheHistory() async {
        let restorer = restorer(items: [], autoPaste: true)

        let outcome = await restorer.restore(text: "Kind regards")

        #expect(outcome == .pasted)
        #expect(board.pasteboard.string(forType: .string) == "Kind regards")
        #expect(effects.writes == 1)
        #expect(effects.pastes == 1)
        #expect(bumps.all.isEmpty)
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
        // Text pastes as before; only files and images refresh the app first.
        #expect(effects.refreshes == 0)
    }

    private func filesItem(_ id: Int64) -> ClipItem {
        ClipItem(
            id: id,
            content: .files(files: [FileRef(path: "/tmp/clipo-folder", sizeBytes: nil)]),
            pinned: false,
            sourceApp: nil,
            createdAt: Rows.copiedAt,
            lastCopiedAt: Rows.copiedAt
        )
    }

    @Test(arguments: [true, false])
    func filesRefreshTheAppInFrontFirst(autoPaste: Bool) async {
        let restorer = restorer(items: [filesItem(9)], autoPaste: autoPaste)

        let outcome = await restorer.restore(Rows.text(9, "clipo-folder"))

        // Also when the user pastes: their own Cmd+V needs the refresh too.
        #expect(outcome == (autoPaste ? .pasted : .restored))
        #expect(effects.refreshes == 1)
        #expect(effects.pastes == (autoPaste ? 1 : 0))
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
