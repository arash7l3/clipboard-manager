import AppKit
import ClipoCore

/// One history item in the popup list: an icon or thumbnail, a title line, a
/// details line, a pin mark, and buttons that rename and delete the item.
/// While the item is renamed, a text field takes the title's place. Built in
/// code, laid out with leading and trailing edges so it mirrors in
/// right-to-left languages.
final class HistoryRowView: NSTableCellView {
    static let reuseIdentifier = NSUserInterfaceItemIdentifier("HistoryRow")
    static let height: CGFloat = 44
    static let iconSize: CGFloat = 32
    static let editorWidth: CGFloat = 180

    let icon = NSImageView()
    let title = NSTextField(labelWithString: "")
    let detail = NSTextField(labelWithString: "")
    private let pin = NSImageView()
    let shortcut = NSTextField(labelWithString: "")
    /// Edits the item's name while it is renamed; hidden otherwise.
    let editor = NSTextField(string: "")
    private let renameButton = NSButton(title: "", target: nil, action: nil)
    private let deleteButton = NSButton(title: "", target: nil, action: nil)
    /// Called when the rename button is clicked.
    var onRename: (@MainActor () -> Void)?
    /// Called when the delete button is clicked.
    var onDelete: (@MainActor () -> Void)?

    init() {
        super.init(frame: NSRect(x: 0, y: 0, width: 300, height: Self.height))
        identifier = Self.reuseIdentifier
        imageView = icon
        textField = title

        icon.imageScaling = .scaleProportionallyUpOrDown
        icon.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 18, weight: .regular)
        icon.contentTintColor = .secondaryLabelColor

        for label in [title, detail] {
            label.lineBreakMode = .byTruncatingTail
            label.maximumNumberOfLines = 1
            label.cell?.truncatesLastVisibleLine = true
            // Long text is cut to the row instead of widening it.
            label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        }
        detail.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        detail.textColor = .secondaryLabelColor

        pin.image = NSImage(systemSymbolName: "pin.fill", accessibilityDescription: nil)
        pin.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 11, weight: .regular)
        pin.contentTintColor = .secondaryLabelColor
        pin.setContentHuggingPriority(.required, for: .horizontal)

        shortcut.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        shortcut.textColor = .tertiaryLabelColor
        shortcut.setContentHuggingPriority(.required, for: .horizontal)
        shortcut.setContentCompressionResistancePriority(.required, for: .horizontal)

        configureEditor()
        configure(renameButton, symbol: "pencil", label: PopupText.renameItem, action: #selector(renameClicked(_:)))
        configure(deleteButton, symbol: "trash", label: PopupText.deleteItem, action: #selector(deleteClicked(_:)))

        let lines = NSStackView(views: [title, editor, detail])
        lines.orientation = .vertical
        lines.alignment = .leading
        lines.spacing = 1

        let row = NSStackView()
        // The buttons sit at the row's trailing end; the rest keeps its place.
        row.setViews([icon, lines, shortcut, pin], in: .leading)
        row.setViews([renameButton, deleteButton], in: .trailing)
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 8
        row.translatesAutoresizingMaskIntoConstraints = false
        addSubview(row)

        NSLayoutConstraint.activate([
            icon.widthAnchor.constraint(equalToConstant: Self.iconSize),
            icon.heightAnchor.constraint(equalToConstant: Self.iconSize),
            row.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 4),
            row.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            row.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])
    }

    private func configureEditor() {
        editor.placeholderString = PopupText.itemNamePlaceholder
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.lineBreakMode = .byTruncatingTail
        editor.usesSingleLineMode = true
        editor.cell?.isScrollable = true
        editor.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        editor.setAccessibilityLabel(PopupText.itemNamePlaceholder)
        editor.widthAnchor.constraint(greaterThanOrEqualToConstant: Self.editorWidth).isActive = true
        editor.isHidden = true
    }

    private func configure(_ button: NSButton, symbol: String, label: String, action: Selector) {
        button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: label)
        button.symbolConfiguration = NSImage.SymbolConfiguration(pointSize: 12, weight: .regular)
        button.imagePosition = .imageOnly
        button.isBordered = false
        button.contentTintColor = .secondaryLabelColor
        button.toolTip = label
        button.setAccessibilityLabel(label)
        button.target = self
        button.action = action
        // Keys stay in the search field.
        button.refusesFirstResponder = true
        button.setContentHuggingPriority(.required, for: .horizontal)
        button.setContentCompressionResistancePriority(.required, for: .horizontal)
    }

    @objc private func renameClicked(_ sender: NSButton) {
        onRename?()
    }

    @objc private func deleteClicked(_ sender: NSButton) {
        onDelete?()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("HistoryRowView is built in code")
    }

    /// Fills the row with `item`; `now` dates its copy time. `quickPaste` is
    /// the digit of its Cmd+digit shortcut, if it has one. `renaming` shows
    /// the editor, holding the item's name, in place of the title.
    func show(_ item: ItemPreview, now: Date, quickPaste: Int? = nil, renaming: Bool = false) {
        shortcut.stringValue = quickPaste.map { "⌘\($0)" } ?? ""
        title.stringValue = PopupText.title(item)
        title.isHidden = renaming
        editor.isHidden = !renaming
        editor.stringValue = renaming ? (item.title ?? PopupText.title(item.content)) : ""
        detail.stringValue = PopupText.detail(item, now: now)
        icon.image = Self.icon(for: item.content)
        pin.isHidden = !item.pinned
        // One spoken sentence per row; its icon and lines are not read again,
        // but its buttons, and the editor while renaming, are reachable.
        setAccessibilityChildren(renaming ? [editor, renameButton, deleteButton] : [renameButton, deleteButton])
        setAccessibilityLabel(PopupText.accessibilityLabel(item, now: now))
    }

    /// A thumbnail for images, the first file's icon for files, and a symbol
    /// for the kind of text.
    private static func icon(for content: PreviewContent) -> NSImage? {
        switch content {
        case .text(_, _, .link):
            return symbol("link")
        case .text(_, _, .code):
            return symbol("chevron.left.forwardslash.chevron.right")
        case .text:
            return symbol("text.alignleft")
        case let .image(_, _, _, thumbnail):
            return thumbnail.flatMap { NSImage(data: $0) } ?? symbol("photo")
        case let .files(files, _):
            guard let first = files.first else { return symbol("doc") }
            return NSWorkspace.shared.icon(forFile: first.path)
        }
    }

    private static func symbol(_ name: String) -> NSImage? {
        NSImage(systemSymbolName: name, accessibilityDescription: nil)
    }
}
