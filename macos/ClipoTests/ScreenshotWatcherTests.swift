import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

@MainActor
struct ScreenshotWatcherTests {
    private let folder: URL

    init() throws {
        folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-screenshots-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    /// Writes a small PNG named `name`, marked as a screenshot when `marked`.
    @discardableResult
    private func writeImage(_ name: String, marked: Bool) throws -> URL {
        let file = folder.appendingPathComponent(name)
        try TestPasteboard.png(width: 4, height: 3).write(to: file)
        if marked {
            let value = Data("1".utf8)
            let result = value.withUnsafeBytes { bytes in
                setxattr(file.path, ScreenshotWatcher.markerAttribute, bytes.baseAddress, value.count, 0, 0)
            }
            #expect(result == 0)
        }
        return file
    }

    /// The first change `stream` yields within `seconds`, or `nil`.
    private func firstChange(
        of stream: AsyncStream<PasteboardChange>,
        within seconds: Double
    ) async -> PasteboardChange? {
        await withTaskGroup(of: PasteboardChange?.self) { group in
            group.addTask {
                for await change in stream {
                    return change
                }
                return nil
            }
            group.addTask {
                try? await Task.sleep(for: .seconds(seconds))
                return nil
            }
            let first = await group.next().flatMap { $0 }
            group.cancelAll()
            return first
        }
    }

    @Test func screenshotsAreToldByTheirMarker() throws {
        let shot = try writeImage("Screenshot 1.png", marked: true)
        try writeImage("photo.png", marked: false)

        let found = ScreenshotWatcher.screenshots(in: folder, since: Date().addingTimeInterval(-60))

        #expect(found.map(\.lastPathComponent) == [shot.lastPathComponent])
    }

    @Test func screenshotsFromBeforeAreSkipped() throws {
        try writeImage("Screenshot 1.png", marked: true)

        #expect(ScreenshotWatcher.screenshots(in: folder, since: Date().addingTimeInterval(60)).isEmpty)
    }

    @Test(arguments: [
        ("a.png", ImageFormat.png), ("b.JPG", .jpeg), ("c.jpeg", .jpeg), ("d.tiff", .tiff), ("e.bmp", .bmp)
    ])
    func keptFormatsComeFromTheExtension(name: String, format: ImageFormat) {
        #expect(ScreenshotWatcher.format(ofFileAt: "/tmp/\(name)") == format)
    }

    @Test func heicAndPdfScreenshotsAreNotKept() {
        #expect(ScreenshotWatcher.format(ofFileAt: "/tmp/a.heic") == nil)
        #expect(ScreenshotWatcher.format(ofFileAt: "/tmp/a.pdf") == nil)
    }

    @Test func theFolderFollowsTheSaveToSetting() throws {
        let name = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
        let settings = try #require(UserDefaults(suiteName: name))
        defer { settings.removePersistentDomain(forName: name) }
        let desktop = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Desktop")

        #expect(ScreenshotWatcher.screenshotFolder(settings: settings).path == desktop.path)

        settings.set("~/Pictures/Shots", forKey: "location")
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        #expect(ScreenshotWatcher.screenshotFolder(settings: settings).path == home + "/Pictures/Shots")
    }

    @Test func aNewScreenshotBecomesAnImageChange() async throws {
        let watcher = ScreenshotWatcher(folder: folder, shouldRead: { _ in true })
        watcher.start()
        defer { watcher.stop() }

        let shot = try writeImage("Screenshot 2.png", marked: true)
        let change = await firstChange(of: watcher.changes, within: 5)

        guard case let .image(data, format)? = change?.content else {
            Issue.record("the screenshot is handed over as an image")
            return
        }
        #expect(format == .png)
        #expect(data == (try Data(contentsOf: shot)))
        #expect(change?.sourceApp?.id == ScreenshotWatcher.sourceApp.id)
    }

    @Test func privateModeKeepsScreenshotsOut() async throws {
        let watcher = ScreenshotWatcher(folder: folder, shouldRead: { _ in false })
        watcher.start()
        defer { watcher.stop() }

        try writeImage("Screenshot 3.png", marked: true)

        #expect(await firstChange(of: watcher.changes, within: 3) == nil)
    }
}
