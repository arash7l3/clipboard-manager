import AppKit
import ClipoCore
import UniformTypeIdentifiers

/// Puts a stored item back on the pasteboard in the forms it was copied in.
enum PasteboardWriter {
    /// Replaces what is on `pasteboard` with `content`. Returns whether the
    /// pasteboard accepted it. Rich text keeps its plain-text form beside it,
    /// so apps that cannot paste markup still get the text. Files none of
    /// whose paths can be written leave the pasteboard as it was.
    @MainActor
    @discardableResult
    static func write(_ content: ItemContent, to pasteboard: NSPasteboard) -> Bool {
        switch content {
        case let .text(text):
            pasteboard.clearContents()
            return writeText(text, to: pasteboard)
        case let .image(image):
            pasteboard.clearContents()
            return pasteboard.setData(image.data, forType: pasteboardType(for: image.format))
        case let .files(files):
            return writeFiles(files, to: pasteboard)
        }
    }

    /// The file URL for a stored path, or `nil` if it names no file. Items
    /// captured before 0.1.6 may hold a Finder file reference path
    /// (`/.file/id=…`), which still finds the file while it exists.
    static func fileURL(forStoredPath path: String) -> URL? {
        if path.hasPrefix(PasteboardReader.referencePrefix) {
            return URL(string: "file://" + path).flatMap(PasteboardReader.pathURL)
        }
        guard path.hasPrefix("/") else { return nil }
        return URL(fileURLWithPath: path)
    }

    @MainActor
    private static func writeFiles(_ files: [FileRef], to pasteboard: NSPasteboard) -> Bool {
        // Paths, not file contents: the files may have moved since, and
        // then the app being pasted into reports it, like for Finder.
        let urls = files.compactMap { fileURL(forStoredPath: $0.path).map { $0 as NSURL } }
        guard !urls.isEmpty else { return false }
        pasteboard.clearContents()
        // AppKit drops a URL it cannot use and still reports success, so
        // check that something landed.
        return pasteboard.writeObjects(urls) && !(pasteboard.pasteboardItems ?? []).isEmpty
    }

    @MainActor
    private static func writeText(_ text: TextContent, to pasteboard: NSPasteboard) -> Bool {
        var written = pasteboard.setString(text.plain, forType: .string)
        if let rich = text.rich {
            let type: NSPasteboard.PasteboardType = switch rich.format {
            case .rtf: .rtf
            case .html: .html
            }
            written = pasteboard.setData(Data(rich.data.utf8), forType: type) && written
        }
        return written
    }

    static func pasteboardType(for format: ImageFormat) -> NSPasteboard.PasteboardType {
        switch format {
        case .png: .png
        case .tiff: .tiff
        case .jpeg: NSPasteboard.PasteboardType(UTType.jpeg.identifier)
        case .bmp: NSPasteboard.PasteboardType(UTType.bmp.identifier)
        }
    }
}
