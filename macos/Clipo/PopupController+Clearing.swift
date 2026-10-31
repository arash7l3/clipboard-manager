import AppKit
import ClipoCore

extension PopupController {
    func configureClearButton() {
        clearButton.target = self
        clearButton.action = #selector(clearClicked(_:))
        // Keys stay in the search field.
        clearButton.refusesFirstResponder = true
        clearButton.setContentHuggingPriority(.required, for: .horizontal)
        clearButton.setContentCompressionResistancePriority(.required, for: .horizontal)
        showClearButton()
    }

    /// Shows the Clear button for the current tab: hidden on Pinned, whose
    /// items clearing never touches, and asking "Clear?" while it waits for
    /// the second click.
    func showClearButton() {
        clearButton.isHidden = !model.canClear
        let armed = clearConfirmation.isArmed
        let label = armed ? PopupText.clearConfirmLabel : PopupText.clearTab(model.category)
        if armed {
            clearButton.image = nil
            clearButton.title = PopupText.clearConfirmTitle
            clearButton.contentTintColor = .systemRed
            clearButton.imagePosition = .noImage
        } else {
            clearButton.title = ""
            clearButton.image = NSImage(systemSymbolName: "trash", accessibilityDescription: label)
            clearButton.contentTintColor = nil
            clearButton.imagePosition = .imageOnly
        }
        clearButton.toolTip = label
        clearButton.setAccessibilityLabel(label)
    }

    /// Back to the plain Clear button, as after switching tabs or closing
    /// the popup.
    func disarmClear() {
        clearTimeout?.cancel()
        clearTimeout = nil
        clearConfirmation.disarm()
        showClearButton()
    }

    @objc private func clearClicked(_ sender: NSButton) {
        _ = view.window?.makeFirstResponder(searchField)
        guard clearConfirmation.press() else {
            armClearTimeout()
            showClearButton()
            return
        }
        let category = model.category
        disarmClear()
        Task {
            await onClear?(category)
            model.refresh()
        }
    }

    /// Disarms the button when the second click does not come in time.
    private func armClearTimeout() {
        clearTimeout?.cancel()
        let wait = clearConfirmation.window
        clearTimeout = Task { [weak self] in
            try? await Task.sleep(for: .seconds(wait))
            guard !Task.isCancelled else { return }
            self?.disarmClear()
        }
    }
}
