import ClipoCore
import Foundation

/// A tab of the popup.
enum PopupTab: Equatable {
    /// A list of history items.
    case history(ClipoCore.Category)
    /// The emoji picker, which lists no history items.
    case emoji
}

/// What the popup shows: the tab, the search text, the rows and which row is
/// selected. Loads rows from the core off the main thread and keeps only the
/// answer to the latest question. On the Emoji tab it keeps the rows of the
/// history tab shown before and leaves the emoji to ``EmojiModel``.
@MainActor
final class PopupModel {
    /// Fetches previews for a query; `nil` if the store failed. Called off the
    /// main thread.
    typealias Loader = @Sendable (Query) -> [ItemPreview]?

    /// The tabs, in display order: the history's, with Emoji before Pinned.
    let tabs: [PopupTab] = {
        var tabs = tabCategories().map(PopupTab.history)
        tabs.insert(.emoji, at: tabs.firstIndex(of: .history(.pinned)) ?? tabs.endIndex)
        return tabs
    }()

    private(set) var tab: PopupTab = .history(.all)
    /// The history tab shown last; the one ``rows`` and loads belong to.
    private(set) var category: ClipoCore.Category = .all
    private(set) var searchText = ""
    private(set) var rows: [ItemPreview] = []
    /// Index into ``rows``; `nil` only when there are no rows.
    private(set) var selection: Int?
    /// The last load failed; ``rows`` is empty.
    private(set) var failed = false
    /// A load is under way; ``rows`` answer an earlier question.
    private(set) var isLoading = false

    /// Called after ``rows`` changed, and the selection with them.
    var onRowsChange: (@MainActor () -> Void)?
    /// Called after the selection moved within the same rows.
    var onSelectionChange: (@MainActor () -> Void)?
    /// Called after ``tab`` was set, whether or not it changed.
    var onTabChange: (@MainActor () -> Void)?

    private let load: Loader
    private let searchDelay: Duration
    private var loading: Task<Void, Never>?
    private var generation = 0

    /// - Parameters:
    ///   - searchDelay: How long typing has to pause before the search runs.
    ///   - load: Fetches previews; see ``Loader``.
    init(searchDelay: Duration = .milliseconds(80), load: @escaping Loader) {
        self.searchDelay = searchDelay
        self.load = load
    }

    var isEmojiTab: Bool {
        tab == .emoji
    }

    /// The current tab has a Clear button: every history tab but Pinned,
    /// whose items clearing never touches.
    var canClear: Bool {
        tab != .history(.pinned) && tab != .emoji
    }

    var selectedRow: ItemPreview? {
        selection.map { rows[$0] }
    }

    /// Back to the `start` tab with no search and no rows until they load, for
    /// a freshly opened popup: Enter must not pick a row left from last time.
    @discardableResult
    func reset(to start: PopupTab = .history(.all)) -> Task<Void, Never> {
        show(start)
        searchText = ""
        let task = reload(keepingSelection: false)
        rows = []
        selection = nil
        failed = false
        onRowsChange?()
        onTabChange?()
        return task
    }

    /// Shows `tab`. A history tab loads its rows; the Emoji tab loads nothing.
    @discardableResult
    func setTab(_ tab: PopupTab) -> Task<Void, Never> {
        show(tab)
        onTabChange?()
        if isEmojiTab {
            return loading ?? Task {}
        }
        return reload(keepingSelection: false)
    }

    @discardableResult
    func setCategory(_ category: ClipoCore.Category) -> Task<Void, Never> {
        setTab(.history(category))
    }

    /// Shows the tab `offset` places after the current one, wrapping around.
    @discardableResult
    func moveTab(by offset: Int) -> Task<Void, Never> {
        let index = tabs.firstIndex(of: tab) ?? 0
        let count = tabs.count
        return setTab(tabs[((index + offset) % count + count) % count])
    }

    /// Sets the search text. On the Emoji tab only the text changes; the
    /// emoji are searched by ``EmojiModel``.
    @discardableResult
    func setSearchText(_ text: String) -> Task<Void, Never> {
        guard text != searchText else { return loading ?? Task {} }
        searchText = text
        guard !isEmojiTab else { return loading ?? Task {} }
        return reload(keepingSelection: false, after: searchDelay)
    }

    /// Loads the rows again, for example after a new copy. The selected item
    /// stays selected if it is still listed. The Emoji tab has nothing to
    /// refresh; the next history tab loads its own rows.
    @discardableResult
    func refresh() -> Task<Void, Never> {
        if isEmojiTab {
            return loading ?? Task {}
        }
        return reload(keepingSelection: true)
    }

    /// Moves the selection `offset` rows down (up if negative), stopping at
    /// the first and last row. Returns whether it moved.
    @discardableResult
    func moveSelection(by offset: Int) -> Bool {
        guard let selection else { return false }
        return select(min(max(selection + offset, 0), rows.count - 1))
    }

    /// Selects the row at `index`. Returns whether the selection changed.
    @discardableResult
    func select(_ index: Int) -> Bool {
        guard rows.indices.contains(index), index != selection else { return false }
        selection = index
        onSelectionChange?()
        return true
    }

    private func show(_ tab: PopupTab) {
        self.tab = tab
        if case let .history(category) = tab {
            self.category = category
        }
    }

    private func reload(keepingSelection: Bool, after delay: Duration = .zero) -> Task<Void, Never> {
        loading?.cancel()
        isLoading = true
        generation += 1
        let current = generation
        let query = Query(category: category, text: searchText)
        let keep = keepingSelection ? selectedRow?.id : nil
        let load = load
        let task = Task {
            if delay > .zero {
                try? await Task.sleep(for: delay)
            }
            guard !Task.isCancelled else { return }
            let found = await Task.detached(priority: .userInitiated) { load(query) }.value
            // A newer question may have been asked while this one ran.
            guard current == generation else { return }
            apply(found, keeping: keep)
        }
        loading = task
        return task
    }

    private func apply(_ found: [ItemPreview]?, keeping id: Int64?) {
        isLoading = false
        failed = found == nil
        rows = found ?? []
        if let id, let index = rows.firstIndex(where: { $0.id == id }) {
            selection = index
        } else {
            selection = rows.isEmpty ? nil : 0
        }
        onRowsChange?()
    }
}
