import ClipoCore
import Foundation

/// What the popup shows: the tab, the search text, the rows and which row is
/// selected. Loads rows from the core off the main thread and keeps only the
/// answer to the latest question.
@MainActor
final class PopupModel {
    /// Fetches previews for a query; `nil` if the store failed. Called off the
    /// main thread.
    typealias Loader = @Sendable (Query) -> [ItemPreview]?

    /// The tabs, in display order.
    let categories = tabCategories()

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

    var selectedRow: ItemPreview? {
        selection.map { rows[$0] }
    }

    /// Back to the `start` tab with no search and no rows until they load, for
    /// a freshly opened popup: Enter must not pick a row left from last time.
    @discardableResult
    func reset(to start: ClipoCore.Category = .all) -> Task<Void, Never> {
        category = start
        searchText = ""
        let task = reload(keepingSelection: false)
        rows = []
        selection = nil
        failed = false
        onRowsChange?()
        return task
    }

    @discardableResult
    func setCategory(_ category: ClipoCore.Category) -> Task<Void, Never> {
        self.category = category
        return reload(keepingSelection: false)
    }

    /// Shows the tab `offset` places after the current one, wrapping around.
    @discardableResult
    func moveCategory(by offset: Int) -> Task<Void, Never> {
        let index = categories.firstIndex(of: category) ?? 0
        let count = categories.count
        return setCategory(categories[((index + offset) % count + count) % count])
    }

    @discardableResult
    func setSearchText(_ text: String) -> Task<Void, Never> {
        guard text != searchText else { return loading ?? Task {} }
        searchText = text
        return reload(keepingSelection: false, after: searchDelay)
    }

    /// Loads the rows again, for example after a new copy. The selected item
    /// stays selected if it is still listed.
    @discardableResult
    func refresh() -> Task<Void, Never> {
        reload(keepingSelection: true)
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
