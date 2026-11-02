import AppKit
import ClipoCore

/// One emoji in the grid. It draws the emoji itself and takes the click, so
/// nothing but this view is hit.
final class EmojiCellView: NSView {
    /// Called when the user clicks the emoji.
    var onPick: (@MainActor () -> Void)?

    private(set) var glyph = ""
    private(set) var isCurrent = false

    override var isFlipped: Bool { true }

    func show(_ emoji: Emoji, current: Bool) {
        glyph = emoji.text
        isCurrent = current
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
        setAccessibilityLabel(emoji.name)
        needsDisplay = true
    }

    func setCurrent(_ current: Bool) {
        guard current != isCurrent else { return }
        isCurrent = current
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        if isCurrent {
            NSColor.controlAccentColor.withAlphaComponent(0.35).setFill()
            NSBezierPath(roundedRect: bounds, xRadius: 6, yRadius: 6).fill()
        }
        let text = NSAttributedString(string: glyph, attributes: [.font: NSFont.systemFont(ofSize: 24)])
        let size = text.size()
        text.draw(at: NSPoint(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2))
    }

    // The popup never activates Clipo, so the first click has to count.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        onPick?()
    }
}

/// A grid cell: an ``EmojiCellView``.
final class EmojiItem: NSCollectionViewItem {
    static let identifier = NSUserInterfaceItemIdentifier("emoji")

    let cell = EmojiCellView()

    override func loadView() {
        view = cell
    }
}

/// The heading above a section of the grid.
final class EmojiHeaderView: NSView {
    static let identifier = NSUserInterfaceItemIdentifier("emojiHeader")

    private let label = NSTextField(labelWithString: "")

    var title: String {
        get { label.stringValue }
        set {
            label.stringValue = newValue
            setAccessibilityLabel(newValue)
        }
    }

    override init(frame: NSRect) {
        super.init(frame: frame)
        // The grid itself stays left to right; the heading follows the language.
        userInterfaceLayoutDirection = NSApp.userInterfaceLayoutDirection
        label.font = .systemFont(ofSize: 11, weight: .semibold)
        label.textColor = .secondaryLabelColor
        label.lineBreakMode = .byTruncatingTail
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 12),
            label.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -12),
            label.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -4)
        ])
        setAccessibilityElement(true)
        setAccessibilityRole(.staticText)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("EmojiHeaderView is built in code")
    }
}

/// The collection view never takes the keyboard: it stays in the search field.
final class EmojiCollectionView: NSCollectionView {
    override var acceptsFirstResponder: Bool { false }
}
