import AppKit
import ApplicationServices
import Carbon.HIToolbox
import os

/// The Accessibility permission, which macOS requires before an app may send
/// key presses to other apps. Clipo needs it only to paste for the user.
enum Accessibility {
    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "paste")

    /// Whether Clipo may send key presses to other apps. Checking does not ask.
    static var isGranted: Bool {
        AXIsProcessTrusted()
    }

    /// Lists Clipo under Privacy & Security > Accessibility and shows the
    /// system's own request. Returns whether the permission is already given.
    @discardableResult
    static func request() -> Bool {
        // The literal behind kAXTrustedCheckOptionPrompt, which Swift 6 does
        // not let us read as a global.
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    /// Sends Cmd+V to the app in front. Needs ``isGranted``; without it the
    /// system drops the events silently.
    static func sendPaste() {
        let source = CGEventSource(stateID: .combinedSessionState)
        let keyCode = CGKeyCode(kVK_ANSI_V)
        guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: false)
        else {
            logger.error("could not build the paste key events")
            return
        }
        // Only Command, whatever modifiers the user may still be holding.
        keyDown.flags = .maskCommand
        keyUp.flags = .maskCommand
        keyDown.post(tap: .cghidEventTap)
        keyUp.post(tap: .cghidEventTap)
    }

    /// Explains why auto-paste needs the permission and offers to grant it.
    /// Blocks until the user answers; the item is already on the clipboard.
    @MainActor
    static func explain() {
        NSApp.activate()
        let alert = NSAlert()
        alert.messageText = String(
            localized: "Allow Clipo to paste for you",
            comment: "Alert title asking for the Accessibility permission"
        )
        alert.informativeText = String(
            localized: """
            Auto-paste presses Command-V in the app you were using, and macOS only allows that after you \
            turn on Clipo in Privacy & Security > Accessibility. Clipo uses it for nothing else. \
            The item is on your clipboard now: press Command-V to paste it yourself.
            """,
            comment: "Alert text explaining the Accessibility permission needed for auto-paste"
        )
        alert.addButton(withTitle: String(
            localized: "Grant Access…",
            comment: "Alert button that asks macOS for the Accessibility permission"
        ))
        alert.addButton(withTitle: String(
            localized: "Not Now",
            comment: "Alert button that keeps auto-paste unavailable for now"
        ))
        if alert.runModal() == .alertFirstButtonReturn {
            request()
        }
    }
}
