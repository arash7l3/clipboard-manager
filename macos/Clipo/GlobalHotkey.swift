import AppKit
import Carbon.HIToolbox
import os

/// A key and modifiers for a global shortcut.
struct KeyCombo: Equatable, Sendable {
    /// Virtual key code, from `Carbon.HIToolbox` (`kVK_…`).
    let keyCode: UInt32
    /// The character the key types, as `NSMenuItem.keyEquivalent` shows it.
    let character: String
    /// Command, Shift, Option and Control.
    let modifiers: NSEvent.ModifierFlags

    /// Opens the popup: Cmd+Shift+V (decision 0004).
    static let showHistory = KeyCombo(keyCode: UInt32(kVK_ANSI_V), character: "v", modifiers: [.command, .shift])

    /// The modifiers as Carbon flags, for `RegisterEventHotKey`.
    var carbonModifiers: UInt32 {
        var flags = 0
        if modifiers.contains(.command) { flags |= cmdKey }
        if modifiers.contains(.shift) { flags |= shiftKey }
        if modifiers.contains(.option) { flags |= optionKey }
        if modifiers.contains(.control) { flags |= controlKey }
        return UInt32(flags)
    }
}

/// One system-wide shortcut, registered with Carbon's hot key API.
///
/// Unlike a global event monitor, a registered hot key needs no Accessibility
/// permission, and the key press is not passed on to the frontmost app.
@MainActor
final class GlobalHotkey {
    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "hotkey")

    /// "CLPO": tells Clipo's hot keys from other apps' in Carbon events.
    private static let signature: OSType = 0x434C_504F
    private static var lastID: UInt32 = 0

    /// Tells this shortcut's presses from other instances'.
    private let id: UInt32
    private let action: @MainActor () -> Void
    private var handler: EventHandlerRef?
    private var hotKey: EventHotKeyRef?

    /// The registered shortcut, if any.
    private(set) var combo: KeyCombo?

    /// - Parameter action: Called on the main thread on every press.
    init(action: @escaping @MainActor () -> Void) {
        Self.lastID += 1
        id = Self.lastID
        self.action = action
    }

    /// Registers `combo`, replacing any shortcut registered before. Returns
    /// whether it worked; it fails when another app holds the same shortcut.
    /// Call ``unregister()`` before releasing this object: Carbon keeps a
    /// pointer to it while a shortcut is registered.
    @discardableResult
    func register(_ combo: KeyCombo) -> Bool {
        unregister()
        guard installHandler() else { return false }

        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(
            combo.keyCode,
            combo.carbonModifiers,
            EventHotKeyID(signature: Self.signature, id: id),
            GetApplicationEventTarget(),
            0,
            &ref
        )
        guard status == OSStatus(noErr), let ref else {
            Self.logger.error("registering the shortcut failed: OSStatus \(status, privacy: .public)")
            unregister()
            return false
        }
        hotKey = ref
        self.combo = combo
        return true
    }

    /// Removes the shortcut and its event handler. Does nothing if none is
    /// registered.
    func unregister() {
        if let hotKey {
            UnregisterEventHotKey(hotKey)
        }
        if let handler {
            RemoveEventHandler(handler)
        }
        hotKey = nil
        handler = nil
        combo = nil
    }

    /// Calls the action, as a press of the shortcut does.
    func fire() {
        action()
    }

    /// Fires if the pressed hot key is this one. Returns whether it was.
    func pressed(signature: OSType, id: UInt32) -> Bool {
        guard hotKey != nil, signature == Self.signature, id == self.id else { return false }
        fire()
        return true
    }

    private func installHandler() -> Bool {
        var pressed = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        var ref: EventHandlerRef?
        let status = InstallEventHandler(
            GetApplicationEventTarget(),
            handleHotKeyEvent,
            1,
            &pressed,
            Unmanaged.passUnretained(self).toOpaque(),
            &ref
        )
        guard status == OSStatus(noErr), let ref else {
            Self.logger.error("installing the shortcut handler failed: OSStatus \(status, privacy: .public)")
            return false
        }
        handler = ref
        return true
    }
}

/// Carbon calls this on the main thread when a hot key of the app is pressed.
/// `userData` is the ``GlobalHotkey`` that installed it. A press of another
/// instance's hot key is passed on to the next handler.
private func handleHotKeyEvent(
    _: EventHandlerCallRef?,
    _ event: EventRef?,
    _ userData: UnsafeMutableRawPointer?
) -> OSStatus {
    let notHandled = OSStatus(eventNotHandledErr)
    guard let event, let userData else { return notHandled }
    var key = EventHotKeyID()
    let status = GetEventParameter(
        event,
        EventParamName(kEventParamDirectObject),
        EventParamType(typeEventHotKeyID),
        nil,
        MemoryLayout<EventHotKeyID>.size,
        nil,
        &key
    )
    guard status == OSStatus(noErr) else { return notHandled }
    let signature = key.signature
    let id = key.id
    let hotkey = Unmanaged<GlobalHotkey>.fromOpaque(userData).takeUnretainedValue()
    let handled = MainActor.assumeIsolated {
        hotkey.pressed(signature: signature, id: id)
    }
    return handled ? OSStatus(noErr) : notHandled
}
