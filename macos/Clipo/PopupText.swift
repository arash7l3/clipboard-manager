import ClipoCore
import Foundation

/// The words the popup shows for tabs and rows.
enum PopupText {
    /// Longest row title, in characters. The row cuts it to its width anyway;
    /// this only keeps the label from laying out a whole page of text.
    static let maxTitleLength = 300

    static func tabTitle(_ category: ClipoCore.Category) -> String {
        switch category {
        case .all:
            String(localized: "All", comment: "Popup tab listing every item")
        case .text:
            String(localized: "Text", comment: "Popup tab listing text items")
        case .images:
            String(localized: "Images", comment: "Popup tab listing images")
        case .files:
            String(localized: "Files", comment: "Popup tab listing copied files")
        case .pinned:
            String(localized: "Pinned", comment: "Popup tab listing pinned items")
        }
    }

    /// The Clear button's tooltip and screen-reader label on `category`'s tab.
    static func clearTab(_ category: ClipoCore.Category) -> String {
        switch category {
        case .text:
            String(localized: "Clear unpinned text", comment: "Clear button tooltip on the Text tab")
        case .images:
            String(localized: "Clear unpinned images", comment: "Clear button tooltip on the Images tab")
        case .files:
            String(localized: "Clear unpinned files", comment: "Clear button tooltip on the Files tab")
        case .all, .pinned:
            String(localized: "Clear all unpinned items", comment: "Clear button tooltip on the All tab")
        }
    }

    /// The Clear button's title after the first click, asking for a second.
    static var clearConfirmTitle: String {
        String(localized: "Clear?", comment: "Clear button label after the first click, asking for a second")
    }

    /// The Clear button's tooltip and screen-reader label after the first click.
    static var clearConfirmLabel: String {
        String(
            localized: "Click again to clear. Pinned items are kept.",
            comment: "Clear button tooltip after the first click"
        )
    }

    /// One line describing the content: the first line of a text, an image's
    /// size, or the first file's name.
    static func title(_ content: PreviewContent) -> String {
        switch content {
        case let .text(snippet, _, _):
            return firstLine(snippet)
        case let .image(_, width, height, _):
            return String(
                localized: "Image \(Int(width)) × \(Int(height))",
                comment: "Popup row title of an image: its width and height in pixels"
            )
        case let .files(files, total):
            let name = files.first.map { fileName($0.path) } ?? ""
            guard total > 1 else { return name }
            return String(
                localized: "\(name) and \(Int(clamping: total - 1)) more",
                comment: "Popup row title of several copied files: the first file's name and how many others"
            )
        }
    }

    /// The first line of `text` that is not blank, cut to ``maxTitleLength``.
    static func firstLine(_ text: String) -> String {
        let line = text
            .split(whereSeparator: \.isNewline)
            .lazy
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .first { !$0.isEmpty } ?? ""
        return String(line.prefix(maxTitleLength))
    }

    /// Where and when the item was copied, for the row's second line.
    static func detail(_ row: ItemPreview, now: Date) -> String {
        let time = copiedTime(row.lastCopiedAt, now: now)
        guard let app = row.sourceApp.map({ $0.name ?? $0.id }) else { return time }
        return String(
            localized: "\(app) · \(time)",
            comment: "Popup row details: the app an item was copied from, then how long ago"
        )
    }

    /// The kind of content, as a screen reader says it.
    static func kind(_ content: PreviewContent) -> String {
        switch content {
        case .text(_, _, .link):
            String(localized: "Link", comment: "Screen-reader name of a copied link")
        case .text(_, _, .code):
            String(localized: "Code", comment: "Screen-reader name of copied source code")
        case .text:
            tabTitle(.text)
        case .image:
            String(localized: "Image", comment: "Screen-reader name of a copied image")
        case .files:
            tabTitle(.files)
        }
    }

    /// Everything a row shows, in one sentence for screen readers.
    static func accessibilityLabel(_ row: ItemPreview, now: Date) -> String {
        var parts = [kind(row.content), title(row.content), detail(row, now: now)]
        if row.pinned {
            parts.append(String(localized: "pinned", comment: "Screen-reader note that an item is pinned"))
        }
        let separator = String(localized: ", ", comment: "Separator between the parts of a screen-reader label")
        return parts.filter { !$0.isEmpty }.joined(separator: separator)
    }

    /// What the list says when it has no rows.
    static func emptyList(failed: Bool, searching: Bool) -> String {
        if failed {
            return String(
                localized: "History unavailable",
                comment: "Menu status when the history could not be opened"
            )
        }
        if searching {
            return String(localized: "No matches", comment: "Popup message when the search finds nothing")
        }
        return String(localized: "No items", comment: "Popup message when a tab is empty")
    }

    /// "5 min. ago", or "now" within the last minute.
    static func copiedTime(_ date: Date, now: Date) -> String {
        if now.timeIntervalSince(date) < 60 {
            return String(localized: "now", comment: "Popup row time of an item copied less than a minute ago")
        }
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .abbreviated
        formatter.dateTimeStyle = .numeric
        return formatter.localizedString(for: date, relativeTo: now)
    }

    private static func fileName(_ path: String) -> String {
        let name = (path as NSString).lastPathComponent
        return name.isEmpty ? path : name
    }
}
