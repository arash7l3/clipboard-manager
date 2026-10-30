import AppKit
import ClipoCore

/// One history item in the popup list: an icon or thumbnail, a title line, a
/// details line and a pin mark. Built in code, laid out with leading and
/// trailing edges so it mirrors in right-to-left languages.
final class HistoryRowView: NSTableCellView {
    static let reuseIdentifier = NSUserInterfaceItemIdentifier("HistoryRow")
    static let height: CGFloat = 44
    static let iconSize: CGFloat = 32

    let icon = NSImageView()
    let title = NSTextField(labelWithString: "")
    let detail = NSTextField(labelWithString: "")
    private let pin = NSImageView()

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

        let lines = NSStackView(views: [title, detail])
        lines.orientation = .vertical
        lines.alignment = .leading
        lines.spacing = 1

        let row = NSStackView(views: [icon, lines, pin])
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

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("HistoryRowView is built in code")
    }

    /// Fills the row with `item`; `now` dates its copy time.
    func show(_ item: ItemPreview, now: Date) {
        title.stringValue = PopupText.title(item.content)
        detail.stringValue = PopupText.detail(item, now: now)
        icon.image = Self.icon(for: item.content)
        pin.isHidden = !item.pinned
        // One spoken sentence per row; its icon and lines are not read again.
        setAccessibilityChildren([])
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
