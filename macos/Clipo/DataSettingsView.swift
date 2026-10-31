import AppKit
import ClipoCore
import SwiftUI
import UniformTypeIdentifiers

/// Export and import of the history and snippets.
struct DataSettingsView: View {
    let model: ExtrasModel
    @State private var message: String?
    @State private var busy = false

    var body: some View {
        Form {
            Section {
                Button("Export History and Snippets…") { Task { await export() } }
                Button("Import…") { Task { await importFile() } }
                if let message {
                    Text(verbatim: message)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            } footer: {
                Text("An export is not encrypted. Anyone who gets the file can read what you copied.")
            }
        }
        .formStyle(.grouped)
        .disabled(!model.isAvailable || busy)
    }

    private static let fileType = UTType(filenameExtension: "clipoexport") ?? .data

    /// Largest file offered to import, as on Windows.
    static let maxImportBytes = 512 * 1024 * 1024

    private func export() async {
        guard confirmExport() else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [Self.fileType]
        panel.nameFieldStringValue = String(
            localized: "Clipo Export.clipoexport",
            comment: "Default file name of an export"
        )
        guard panel.runModal() == .OK, let url = panel.url else { return }
        busy = true
        defer { busy = false }
        guard let data = await model.exportData() else {
            message = String(localized: "The export failed.", comment: "Settings message when exporting failed")
            return
        }
        do {
            try Self.writePrivately(data, to: url)
            message = String(localized: "Exported.", comment: "Settings message after a successful export")
        } catch {
            message = String(
                localized: "The file could not be written.",
                comment: "Settings message when the export file could not be saved"
            )
        }
    }

    /// Writes `data` to a new file at `url` that only the user can read from
    /// the moment it exists: it holds clipboard content. Replaces a file
    /// already there, which the save panel asked about.
    static func writePrivately(_ data: Data, to url: URL) throws {
        try? FileManager.default.removeItem(at: url)
        // O_EXCL: never through a link someone left at the path.
        let descriptor = Darwin.open(url.path, O_WRONLY | O_CREAT | O_EXCL, 0o600)
        guard descriptor >= 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        let handle = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        do {
            try handle.write(contentsOf: data)
            try handle.close()
        } catch {
            try? FileManager.default.removeItem(at: url)
            throw error
        }
    }

    private func confirmExport() -> Bool {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = String(
            localized: "Export without encryption?",
            comment: "Alert title before exporting the history"
        )
        alert.informativeText = String(
            localized: """
            The file holds everything you copied, possibly including passwords, as plain data. \
            Keep it somewhere safe and delete it when you no longer need it.
            """,
            comment: "Alert text before exporting the history"
        )
        alert.addButton(withTitle: String(localized: "Export", comment: "Alert button that exports the history"))
        alert.addButton(withTitle: String(localized: "Cancel", comment: "Alert button that cancels"))
        return alert.runModal() == .alertFirstButtonReturn
    }

    private func importFile() async {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        // A file this large is not an export; reading it would only fill memory.
        let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        guard size <= Self.maxImportBytes else {
            message = Self.text(for: .invalid)
            return
        }
        busy = true
        defer { busy = false }
        guard let data = try? Data(contentsOf: url) else {
            message = String(localized: "The file could not be read.", comment: "Settings message when import failed")
            return
        }
        message = Self.text(for: await model.importData(data))
    }

    static func text(for result: ExtrasModel.ImportResult) -> String {
        switch result {
        case let .imported(summary):
            let items = Int(clamping: summary.itemsAdded)
            let snippets = Int(clamping: summary.snippetsAdded)
            return String(
                localized: "Imported \(items) items and \(snippets) snippets.",
                comment: "Settings message after an import: how many items and snippets were added"
            )
        case .invalid:
            return String(
                localized: "This is not a Clipo export, or it is damaged.",
                comment: "Settings message when the import file is not valid"
            )
        case .tooNew:
            return String(
                localized: "This export is from a newer version of Clipo.",
                comment: "Settings message when the import file is from a newer version"
            )
        case .failed:
            return String(localized: "The import failed.", comment: "Settings message when importing failed")
        }
    }
}
