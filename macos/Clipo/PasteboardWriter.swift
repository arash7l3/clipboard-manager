import AppKit
import ClipoCore
import UniformTypeIdentifiers

/// Puts a stored item back on the pasteboard in the forms it was copied in.
enum PasteboardWriter {
    /// Replaces what is on `pasteboard` with `content`. Returns whether the
    /// pasteboard accepted it. Rich text keeps its plain-text form beside it,
    /// so apps that cannot paste markup still get the text.
    @MainActor
    @discardableResult
    static func write(_ content: ItemContent, to pasteboard: NSPasteboard) -> Bool {
        pasteboard.clearContents()
        switch content {
        case let .text(text):
            return writeText(text, to: pasteboard)
        case let .image(image):
            return pasteboard.setData(image.data, forType: pasteboardType(for: image.format))
        case let .files(files):
            // Paths, not file contents: the files may have moved since, and
            // then the app being pasted into reports it, like for Finder.
            let urls = files.map { NSURL(fileURLWithPath: $0.path) }
            return !urls.isEmpty && pasteboard.writeObjects(urls)
        }
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
