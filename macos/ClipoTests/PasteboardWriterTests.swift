import AppKit
@testable import Clipo
import ClipoCore
import Testing

@MainActor
struct PasteboardWriterTests {
    private let board = TestPasteboard()

    @Test func plainTextReplacesWhatWasThere() {
        board.set("old", extra: [.html: Data("<b>old</b>".utf8)])

        let written = PasteboardWriter.write(.text(text: TextContent(plain: "new", rich: nil)), to: board.pasteboard)

        #expect(written)
        #expect(board.pasteboard.string(forType: .string) == "new")
        #expect(board.pasteboard.data(forType: .html) == nil)
    }

    @Test func richTextComesBackBesideThePlainText() {
        let rich = RichText(format: .html, data: "<b>bold</b>")

        PasteboardWriter.write(.text(text: TextContent(plain: "bold", rich: rich)), to: board.pasteboard)

        #expect(board.pasteboard.string(forType: .string) == "bold")
        #expect(board.pasteboard.data(forType: .html) == Data("<b>bold</b>".utf8))
        #expect(board.pasteboard.data(forType: .rtf) == nil)
    }

    @Test func rtfGoesOutAsRtf() {
        let rich = RichText(format: .rtf, data: "{\\rtf1 hi}")

        PasteboardWriter.write(.text(text: TextContent(plain: "hi", rich: rich)), to: board.pasteboard)

        #expect(board.pasteboard.data(forType: .rtf) == Data("{\\rtf1 hi}".utf8))
    }

    @Test func imageKeepsItsEncoding() throws {
        let data = try TestPasteboard.png(width: 4, height: 3)
        let image = ImageContent(format: .png, width: 4, height: 3, data: data, thumbnail: nil)

        PasteboardWriter.write(.image(image: image), to: board.pasteboard)

        #expect(board.pasteboard.data(forType: .png) == data)
        #expect(board.pasteboard.string(forType: .string) == nil)
    }

    @Test func filesComeBackAsFileUrlsInOrder() {
        let files = [FileRef(path: "/tmp/b.txt", sizeBytes: 1), FileRef(path: "/tmp/a.txt", sizeBytes: nil)]

        let written = PasteboardWriter.write(.files(files: files), to: board.pasteboard)

        let urls = board.pasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL]
        #expect(written)
        #expect(urls?.map(\.path) == ["/tmp/b.txt", "/tmp/a.txt"])
    }

    @Test func restoredItemReadsBackLikeTheOriginal() throws {
        // What the reader captures from a copy is what the writer restores.
        board.set("round trip", extra: [.html: Data("<i>round trip</i>".utf8)])
        let content = try #require(PasteboardReader.content(of: board.pasteboard))
        let capture = try #require(
            PasteboardReader.capture(from: content, sourceApp: nil, at: Date(), maxItemBytes: 1000)
        )

        PasteboardWriter.write(capture.content, to: board.pasteboard)

        #expect(PasteboardReader.content(of: board.pasteboard) == content)
    }
}
