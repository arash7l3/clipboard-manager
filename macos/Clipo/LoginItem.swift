import Foundation
import os
import ServiceManagement

/// Whether macOS starts Clipo when the user logs in.
enum LoginItem {
    private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "login")

    static var status: SMAppService.Status {
        SMAppService.mainApp.status
    }

    /// On, or waiting for the user's approval in System Settings.
    static var isOn: Bool {
        status == .enabled || status == .requiresApproval
    }

    /// The user must still allow Clipo in System Settings > General > Login Items.
    static var needsApproval: Bool {
        status == .requiresApproval
    }

    /// Adds or removes Clipo from the login items. Returns whether it worked.
    @discardableResult
    static func setOn(_ wanted: Bool) -> Bool {
        let service = SMAppService.mainApp
        do {
            if wanted {
                try service.register()
            } else if service.status != .notRegistered {
                try service.unregister()
            }
            return true
        } catch {
            logger.error("changing the login item failed: \(String(describing: error), privacy: .public)")
            return false
        }
    }

    static func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
