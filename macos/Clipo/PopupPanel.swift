import AppKit

/// The floating window of the history popup.
///
/// A non-activating panel takes the keyboard without making Clipo the active
/// app, so the app the user was working in stays frontmost and keeps its
/// focused text field: that is where an item is pasted (step 2.4).
final class PopupPanel: NSPanel {
    /// Called when the panel should go away: Esc, or a click outside it.
    var onDismiss: (@MainActor () -> Void)?
    /// Called with the row index (0 to 8) for Cmd+1 to Cmd+9.
    var onQuickPaste: (@MainActor (Int) -> Void)?
    /// Called for Cmd+P: pin or unpin the selected item.
    var onTogglePin: (@MainActor () -> Void)?

    /// - Parameter content: The view to show, filling the panel.
    init(content: NSView) {
        super.init(
            contentRect: NSRect(origin: .zero, size: Self.defaultSize),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: true
        )
        isFloatingPanel = true
        level = .popUpMenu
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = false
        isMovable = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        animationBehavior = .utilityWindow
        // Shown on whichever Space and full-screen app is current; not part of
        // Cmd+` window cycling.
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        isReleasedWhenClosed = false

        let background = NSVisualEffectView()
        background.material = .popover
        background.blendingMode = .behindWindow
        background.state = .active
        background.wantsLayer = true
        background.layer?.cornerRadius = Self.cornerRadius
        background.layer?.masksToBounds = true
        contentView = background

        content.translatesAutoresizingMaskIntoConstraints = false
        background.addSubview(content)
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: background.topAnchor),
            content.bottomAnchor.constraint(equalTo: background.bottomAnchor),
            content.leadingAnchor.constraint(equalTo: background.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: background.trailingAnchor)
        ])
        setAccessibilityLabel(String(
            localized: "Clipboard history",
            comment: "Screen-reader label of the popup window"
        ))
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("PopupPanel is built in code")
    }

    static let defaultSize = NSSize(width: 380, height: 460)
    static let cornerRadius: CGFloat = 12

    // Borderless windows refuse key status by default; the popup needs the
    // keyboard for search and arrows.
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if modifiers == .command,
           let digit = event.charactersIgnoringModifiers.flatMap({ Int($0) }),
           (1...9).contains(digit) {
            onQuickPaste?(digit - 1)
            return true
        }
        if modifiers == .command, event.charactersIgnoringModifiers == "p" {
            onTogglePin?()
            return true
        }
        return super.performKeyEquivalent(with: event)
    }

    override func cancelOperation(_ sender: Any?) {
        onDismiss?()
    }

    override func resignKey() {
        super.resignKey()
        onDismiss?()
    }
}

/// Where the popup opens.
enum PopupPlacement {
    /// The panel's frame for a pointer at `cursor` on a screen whose usable area
    /// is `visible`. The panel hangs from the pointer, to its right in a
    /// left-to-right layout and to its left in a right-to-left one, and is
    /// moved as little as needed to fit on the screen. If it is larger than the
    /// screen, its top and leading edges stay visible.
    static func frame(
        size: NSSize,
        cursor: NSPoint,
        visible: NSRect,
        layoutDirection: NSUserInterfaceLayoutDirection
    ) -> NSRect {
        let preferredX = layoutDirection == .rightToLeft ? cursor.x - size.width : cursor.x
        let preferredY = cursor.y - size.height

        // Clamped against the trailing edge first, so the leading edge wins.
        let originX: CGFloat
        if layoutDirection == .rightToLeft {
            originX = min(max(preferredX, visible.minX), visible.maxX - size.width)
        } else {
            originX = max(min(preferredX, visible.maxX - size.width), visible.minX)
        }
        // Clamped against the bottom first, so the top wins.
        let originY = min(max(preferredY, visible.minY), visible.maxY - size.height)
        return NSRect(x: originX, y: originY, width: size.width, height: size.height)
    }

    /// The usable area of the screen under `cursor`, or of the main screen.
    @MainActor
    static func visibleFrame(around cursor: NSPoint) -> NSRect? {
        let screen = NSScreen.screens.first { NSMouseInRect(cursor, $0.frame, false) } ?? NSScreen.main
        return screen?.visibleFrame
    }
}
