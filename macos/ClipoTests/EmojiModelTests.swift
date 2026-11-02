import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// Emoji fixtures.
enum Emojis {
    static func make(_ text: String, _ name: String, _ group: EmojiGroup) -> Emoji {
        Emoji(text: text, name: name, group: group)
    }

    /// Five smileys and three animals.
    static let catalog = [
        EmojiSection(group: .smileysAndPeople, emoji: [
            make("😀", "grinning face", .smileysAndPeople),
            make("😃", "grinning face with big eyes", .smileysAndPeople),
            make("😄", "grinning face with smiling eyes", .smileysAndPeople),
            make("😁", "beaming face with smiling eyes", .smileysAndPeople),
            make("😆", "grinning squinting face", .smileysAndPeople)
        ]),
        EmojiSection(group: .animalsAndNature, emoji: [
            make("🐶", "dog face", .animalsAndNature),
            make("🐱", "cat face", .animalsAndNature),
            make("🐭", "mouse face", .animalsAndNature)
        ])
    ]

    /// A search that finds the emoji whose name contains the text.
    static func search(_ text: String) -> [Emoji] {
        catalog.flatMap(\.emoji).filter { $0.name.contains(text) }
    }
}

/// Reports which emoji the recent loader should answer with.
final class FakeRecent: @unchecked Sendable {
    private let lock = NSLock()
    private var emoji: [Emoji]

    init(_ emoji: [Emoji] = []) {
        self.emoji = emoji
    }

    func set(_ emoji: [Emoji]) {
        lock.withLock { self.emoji = emoji }
    }

    func load() -> [Emoji] {
        lock.withLock { emoji }
    }
}

@MainActor
struct EmojiModelTests {
    private func makeModel(recent: FakeRecent = FakeRecent()) -> EmojiModel {
        let model = EmojiModel(catalog: Emojis.catalog, search: Emojis.search) { recent.load() }
        model.columns = 2
        model.pageRows = 2
        return model
    }

    private func texts(_ section: EmojiGridSection) -> [String] {
        section.emoji.map(\.text)
    }

    @Test func listsTheGroupsInOrderAndSelectsTheFirstEmoji() {
        let model = makeModel()

        #expect(model.sections.map(\.kind) == [.group(.smileysAndPeople), .group(.animalsAndNature)])
        #expect(model.groups == [.smileysAndPeople, .animalsAndNature])
        #expect(model.selection == EmojiPosition(section: 0, item: 0))
        #expect(model.selectedEmoji?.text == "😀")
    }

    @Test func recentEmojiComeFirst() async {
        let recent = FakeRecent([Emojis.catalog[1].emoji[2], Emojis.catalog[0].emoji[0]])
        let model = makeModel(recent: recent)
        var changes = 0
        model.onSectionsChange = { changes += 1 }

        await model.reloadRecent().value

        #expect(model.sections.map(\.kind) == [.recent, .group(.smileysAndPeople), .group(.animalsAndNature)])
        #expect(texts(model.sections[0]) == ["🐭", "😀"])
        #expect(model.selectedEmoji?.text == "🐭")
        #expect(changes == 1)
        #expect(model.sectionIndex(of: .recent) == 0)
        #expect(model.sectionIndex(of: .results) == nil)
    }

    @Test func searchShowsOneBlockOfResults() {
        let recent = FakeRecent([Emojis.catalog[0].emoji[0]])
        let model = makeModel(recent: recent)

        model.setSearchText("face")
        #expect(model.sections.map(\.kind) == [.results])
        #expect(model.sections[0].emoji.count == 8)

        model.setSearchText(" cat ")
        #expect(texts(model.sections[0]) == ["🐱"])
        #expect(model.selectedEmoji?.text == "🐱")

        model.setSearchText("zzz")
        #expect(model.sections.isEmpty)
        #expect(model.selection == nil)
        #expect(model.selectedEmoji == nil)
        #expect(!model.move(.step(1)))

        model.setSearchText("")
        #expect(model.sections.count == 2)
        #expect(model.selection == EmojiPosition(section: 0, item: 0))
    }

    @Test func stepsGoAcrossRowsAndSections() {
        let model = makeModel()

        #expect(!model.move(.step(-1)))
        #expect(model.move(.step(1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 1))
        #expect(model.move(.step(4)))
        #expect(model.selection == EmojiPosition(section: 1, item: 0))
        #expect(model.move(.step(-1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 4))
        #expect(model.move(.step(100)))
        #expect(model.selection == EmojiPosition(section: 1, item: 2))
        #expect(!model.move(.step(1)))
    }

    @Test func rowsKeepTheColumn() {
        let model = makeModel()
        model.select(EmojiPosition(section: 0, item: 1))

        // Section 0 has rows [0 1] [2 3] [4]; section 1 has [0 1] [2].
        #expect(model.move(.rows(1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 3))
        #expect(model.move(.rows(1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 4))
        #expect(model.move(.rows(1)))
        #expect(model.selection == EmojiPosition(section: 1, item: 0))
        #expect(model.move(.rows(1)))
        #expect(model.selection == EmojiPosition(section: 1, item: 2))
        #expect(!model.move(.rows(1)))

        #expect(model.move(.rows(-1)))
        #expect(model.selection == EmojiPosition(section: 1, item: 0))
        #expect(model.move(.rows(-1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 4))
        #expect(model.move(.rows(-2)))
        #expect(model.selection == EmojiPosition(section: 0, item: 0))
        #expect(!model.move(.rows(-1)))
    }

    @Test func pagesAndEndsMoveFar() {
        let model = makeModel()

        #expect(model.move(.pages(1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 4))
        #expect(model.move(.last))
        #expect(model.selection == EmojiPosition(section: 1, item: 2))
        #expect(!model.move(.last))
        #expect(model.move(.pages(-1)))
        #expect(model.selection == EmojiPosition(section: 0, item: 0))
        #expect(!model.move(.first))
        model.select(EmojiPosition(section: 0, item: 3))
        #expect(model.move(.first))
        #expect(model.selection == EmojiPosition(section: 0, item: 0))
    }

    @Test func selectionMovesAreReportedAndBadOnesRefused() {
        let model = makeModel()
        var moves = 0
        model.onSelectionChange = { moves += 1 }

        model.move(.step(1))
        model.select(EmojiPosition(section: 0, item: 1))
        #expect(!model.select(EmojiPosition(section: 5, item: 0)))
        #expect(!model.select(EmojiPosition(section: 0, item: 9)))
        #expect(model.select(EmojiPosition(section: 1, item: 1)))

        #expect(moves == 2)
        #expect(model.selectedEmoji?.text == "🐱")
    }

    @Test func newestRecentAnswerWins() async {
        let recent = FakeRecent([Emojis.catalog[0].emoji[1]])
        let model = makeModel(recent: recent)

        model.reloadRecent()
        recent.set([Emojis.catalog[0].emoji[2]])
        await model.reloadRecent().value

        #expect(texts(model.sections[0]) == ["😄"])
    }
}
