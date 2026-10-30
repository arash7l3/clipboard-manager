import AppKit
import Carbon.HIToolbox
@testable import Clipo
import ClipoCore
import Testing

struct PopupTextTests {
    private let now = Rows.copiedAt

    @Test func textTitleIsTheFirstNonBlankLine() {
        let content = PreviewContent.text(snippet: "\n  \n  سلام دنیا  \nsecond", truncated: false, hint: .plain)
        #expect(PopupText.title(content) == "سلام دنیا")
    }

    @Test func longTitlesAreCut() {
        let content = PreviewContent.text(snippet: String(repeating: "a", count: 1000), truncated: true, hint: .plain)
        #expect(PopupText.title(content).count == PopupText.maxTitleLength)
    }

    @Test func imageTitleHasItsSize() {
        let title = PopupText.title(.image(format: .png, width: 640, height: 480, thumbnail: nil))
        #expect(title.contains("640"))
        #expect(title.contains("480"))
    }

    @Test func filesTitleNamesTheFirstAndCountsTheRest() {
        let one = [FileRef(path: "/Users/me/report.pdf", sizeBytes: 10)]
        #expect(PopupText.title(.files(files: one, total: 1)) == "report.pdf")

        let several = PopupText.title(.files(files: one, total: 4))
        #expect(several.contains("report.pdf"))
        #expect(several.contains("3"))
    }

    @Test func detailNamesTheAppAndTheTime() {
        let named = Rows.text(1, "x", app: SourceApp(id: "com.apple.Safari", name: "Safari"))
        #expect(PopupText.detail(named, now: now).hasPrefix("Safari"))

        let unnamed = Rows.text(1, "x", app: SourceApp(id: "com.example.tool", name: nil))
        #expect(PopupText.detail(unnamed, now: now).hasPrefix("com.example.tool"))

        let anonymous = Rows.text(1, "x")
        #expect(PopupText.detail(anonymous, now: now) == PopupText.copiedTime(Rows.copiedAt, now: now))
    }

    @Test func recentCopiesAreNow() {
        let justNow = PopupText.copiedTime(now.addingTimeInterval(-30), now: now)
        let earlier = PopupText.copiedTime(now.addingTimeInterval(-3600), now: now)
        #expect(justNow != earlier)
        #expect(earlier.contains("1"))
    }

    @Test func accessibilityLabelSaysKindTitleAndPin() {
        let link = Rows.text(1, "https://example.com", pinned: true, hint: .link)
        let label = PopupText.accessibilityLabel(link, now: now)
        #expect(label.hasPrefix(PopupText.kind(link.content)))
        #expect(label.contains("https://example.com"))
        #expect(label.hasSuffix(String(localized: "pinned")))

        let kinds = Set([TextHint.plain, .link, .code].map {
            PopupText.kind(.text(snippet: "x", truncated: false, hint: $0))
        })
        #expect(kinds.count == 3)
    }

    @Test func everyTabHasItsOwnTitle() {
        let titles = Set(tabCategories().map(PopupText.tabTitle))
        #expect(titles.count == 5)
    }

    @Test func emptyListExplainsWhy() {
        let messages: Set = [
            PopupText.emptyList(failed: true, searching: false),
            PopupText.emptyList(failed: false, searching: true),
            PopupText.emptyList(failed: false, searching: false)
        ]
        #expect(messages.count == 3)
    }
}

struct PopupPlacementTests {
    private let size = NSSize(width: 300, height: 400)
    private let screen = NSRect(x: 0, y: 0, width: 1440, height: 875)

    private func frame(
        _ cursorX: CGFloat,
        _ cursorY: CGFloat,
        _ direction: NSUserInterfaceLayoutDirection = .leftToRight
    ) -> NSRect {
        PopupPlacement.frame(
            size: size,
            cursor: NSPoint(x: cursorX, y: cursorY),
            visible: screen,
            layoutDirection: direction
        )
    }

    @Test func hangsFromThePointer() {
        #expect(frame(500, 600) == NSRect(x: 500, y: 200, width: 300, height: 400))
        #expect(frame(500, 600, .rightToLeft) == NSRect(x: 200, y: 200, width: 300, height: 400))
    }

    @Test func staysOnScreen() {
        #expect(frame(1400, 600).origin.x == 1140)
        #expect(frame(500, 100).origin.y == 0)
        #expect(frame(10, 600, .rightToLeft).origin.x == 0)
        #expect(frame(500, 900).maxY == 875)
    }

    @Test func keepsTheTopAndLeadingEdgeOfAnOversizedPopup() {
        let small = NSRect(x: 0, y: 0, width: 200, height: 300)
        let left = PopupPlacement.frame(size: size, cursor: .zero, visible: small, layoutDirection: .leftToRight)
        #expect(left.minX == 0)
        #expect(left.maxY == 300)

        let right = PopupPlacement.frame(size: size, cursor: .zero, visible: small, layoutDirection: .rightToLeft)
        #expect(right.maxX == 200)
    }
}

@MainActor
struct GlobalHotkeyTests {
    @Test func defaultShortcutIsCommandShiftV() {
        let combo = KeyCombo.showHistory
        #expect(combo.keyCode == UInt32(kVK_ANSI_V))
        #expect(combo.carbonModifiers == UInt32(cmdKey | shiftKey))
        #expect(combo.character == "v")
    }

    @Test func everyModifierMapsToCarbon() {
        let combo = KeyCombo(keyCode: 0, character: "a", modifiers: [.command, .shift, .option, .control])
        #expect(combo.carbonModifiers == UInt32(cmdKey | shiftKey | optionKey | controlKey))
    }

    @Test func firingCallsTheAction() {
        var presses = 0
        let hotkey = GlobalHotkey { presses += 1 }
        hotkey.fire()
        #expect(presses == 1)
    }

    @Test func registersAndUnregisters() {
        // An unusual combination, so it does not clash with anything on the Mac.
        let combo = KeyCombo(keyCode: UInt32(kVK_F19), character: "", modifiers: [.command, .option, .control, .shift])
        let hotkey = GlobalHotkey {}
        #expect(hotkey.register(combo))
        #expect(hotkey.combo == combo)
        hotkey.unregister()
        #expect(hotkey.combo == nil)
    }
}
