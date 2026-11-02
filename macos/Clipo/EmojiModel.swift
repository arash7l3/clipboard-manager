import ClipoCore
import Foundation

/// A block of emoji in the grid, under one heading.
struct EmojiGridSection {
    enum Kind: Equatable {
        /// Emoji the user picked lately.
        case recent
        case group(EmojiGroup)
        /// What a search found.
        case results
    }

    let kind: Kind
    let emoji: [Emoji]
}

/// An emoji's place in the grid.
struct EmojiPosition: Equatable {
    var section: Int
    var item: Int

    var indexPath: IndexPath {
        IndexPath(item: item, section: section)
    }
}

/// What the emoji tab shows: the sections, the search text and which emoji is
/// selected. The sections are the recent emoji and the picker's groups, or,
/// while searching, one block of results. Moves follow the grid: each section
/// starts on a new row, `columns` emoji to a row.
@MainActor
final class EmojiModel {
    /// Fetches the recently used emoji. Called off the main thread.
    typealias RecentLoader = @Sendable () -> [Emoji]

    enum Move: Equatable {
        /// Emoji by emoji, across rows and sections; negative goes back.
        case step(Int)
        /// Rows up (negative) or down.
        case rows(Int)
        /// Pages up (negative) or down.
        case pages(Int)
        case first
        case last
    }

    /// The groups the picker offers, in order.
    let groups: [EmojiGroup]

    private(set) var sections: [EmojiGridSection] = []
    /// `nil` only when there are no emoji.
    private(set) var selection: EmojiPosition?
    private(set) var searchText = ""

    /// Emoji to a row; the view sets it from its width.
    var columns = 9 {
        didSet { columns = max(1, columns) }
    }

    /// Rows a page move goes; the view sets it from its height.
    var pageRows = 5 {
        didSet { pageRows = max(1, pageRows) }
    }

    /// Called after ``sections`` changed, and the selection with them.
    var onSectionsChange: (@MainActor () -> Void)?
    /// Called after the selection moved within the same sections.
    var onSelectionChange: (@MainActor () -> Void)?

    private let catalog: [EmojiSection]
    private let search: (String) -> [Emoji]
    private let loadRecent: RecentLoader
    private var recent: [Emoji] = []
    private var generation = 0

    /// - Parameters:
    ///   - catalog: The picker's sections, in order.
    ///   - search: Finds the emoji for the words typed; see ``searchEmoji(text:)``.
    ///   - loadRecent: Fetches the recently used emoji, most recent first.
    init(
        catalog: [EmojiSection] = emojiSections(),
        search: @escaping (String) -> [Emoji] = { searchEmoji(text: $0) },
        loadRecent: @escaping RecentLoader = { [] }
    ) {
        self.catalog = catalog
        self.search = search
        self.loadRecent = loadRecent
        groups = catalog.map(\.group)
        rebuild()
    }

    var selectedEmoji: Emoji? {
        selection.flatMap(emoji(at:))
    }

    /// The emoji at `position`, or `nil` if there is none.
    func emoji(at position: EmojiPosition) -> Emoji? {
        guard sections.indices.contains(position.section),
              sections[position.section].emoji.indices.contains(position.item)
        else { return nil }
        return sections[position.section].emoji[position.item]
    }

    /// The index of the section of `kind`, if the grid has one.
    func sectionIndex(of kind: EmojiGridSection.Kind) -> Int? {
        sections.firstIndex { $0.kind == kind }
    }

    /// Searches the emoji for `text`, or lists the groups again if it is blank.
    func setSearchText(_ text: String) {
        guard text != searchText else { return }
        searchText = text
        rebuild()
        onSectionsChange?()
    }

    /// Loads the recent emoji again, for example when the popup opens. They
    /// are listed first while there is no search.
    @discardableResult
    func reloadRecent() -> Task<Void, Never> {
        generation += 1
        let current = generation
        let load = loadRecent
        return Task {
            let found = await Task.detached(priority: .userInitiated) { load() }.value
            guard current == generation else { return }
            recent = found
            rebuild()
            onSectionsChange?()
        }
    }

    /// Selects `position`. Returns whether the selection changed.
    @discardableResult
    func select(_ position: EmojiPosition) -> Bool {
        guard emoji(at: position) != nil, position != selection else { return false }
        selection = position
        onSelectionChange?()
        return true
    }

    /// Moves the selection, stopping at the first and last emoji. Returns
    /// whether it moved.
    @discardableResult
    func move(_ move: Move) -> Bool {
        guard let current = selection else { return false }
        let target: EmojiPosition?
        switch move {
        case let .step(offset): target = position(from: current, step: offset)
        case let .rows(count): target = position(from: current, rows: count)
        case let .pages(count): target = position(from: current, rows: count * pageRows)
        case .first: target = EmojiPosition(section: 0, item: 0)
        case .last: target = lastPosition
        }
        return target.map(select) ?? false
    }

    private var lastPosition: EmojiPosition? {
        guard let last = sections.last else { return nil }
        return EmojiPosition(section: sections.count - 1, item: last.emoji.count - 1)
    }

    private func rebuild() {
        let text = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty {
            var built: [EmojiGridSection] = []
            if !recent.isEmpty {
                built.append(EmojiGridSection(kind: .recent, emoji: recent))
            }
            sections = built + catalog.map { EmojiGridSection(kind: .group($0.group), emoji: $0.emoji) }
        } else {
            let found = search(text)
            sections = found.isEmpty ? [] : [EmojiGridSection(kind: .results, emoji: found)]
        }
        selection = sections.isEmpty ? nil : EmojiPosition(section: 0, item: 0)
    }

    private func position(from start: EmojiPosition, step offset: Int) -> EmojiPosition? {
        let total = sections.reduce(0) { $0 + $1.emoji.count }
        var remaining = min(max(flatIndex(of: start) + offset, 0), total - 1)
        for (index, section) in sections.enumerated() {
            if remaining < section.emoji.count {
                return EmojiPosition(section: index, item: remaining)
            }
            remaining -= section.emoji.count
        }
        return nil
    }

    private func flatIndex(of position: EmojiPosition) -> Int {
        sections[..<position.section].reduce(0) { $0 + $1.emoji.count } + position.item
    }

    private func position(from start: EmojiPosition, rows: Int) -> EmojiPosition? {
        var current = start
        for _ in 0..<abs(rows) {
            guard let next = rows < 0 ? rowAbove(current) : rowBelow(current) else { break }
            current = next
        }
        return current
    }

    /// The emoji below `position` in the same column; in a short last row, the
    /// last emoji; past the last row, the next section's first row.
    private func rowBelow(_ position: EmojiPosition) -> EmojiPosition? {
        let count = sections[position.section].emoji.count
        if position.item / columns < (count - 1) / columns {
            return EmojiPosition(section: position.section, item: min(position.item + columns, count - 1))
        }
        guard position.section + 1 < sections.count else { return nil }
        let next = sections[position.section + 1].emoji.count
        return EmojiPosition(section: position.section + 1, item: min(position.item % columns, next - 1))
    }

    /// The emoji above `position` in the same column; above the first row, the
    /// previous section's last row.
    private func rowAbove(_ position: EmojiPosition) -> EmojiPosition? {
        if position.item >= columns {
            return EmojiPosition(section: position.section, item: position.item - columns)
        }
        guard position.section > 0 else { return nil }
        let count = sections[position.section - 1].emoji.count
        let lastRowStart = (count - 1) / columns * columns
        return EmojiPosition(
            section: position.section - 1,
            item: min(lastRowStart + position.item % columns, count - 1)
        )
    }
}
