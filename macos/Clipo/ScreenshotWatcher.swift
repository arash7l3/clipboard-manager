import AppKit
import ClipoCore
import os

/// Adds new screenshots to the history.
///
/// By default macOS saves a screenshot as a file and puts nothing on the
/// clipboard, so the pasteboard watcher never sees it. This watches the folder
/// the Screenshot app saves to (its Save To setting, the Desktop by default)
/// and hands each new screenshot over as an image, like a copy made by the
/// Screenshot app. Screenshots are told from other files by the marker macOS
/// gives them (`kMDItemIsScreenCapture`). Only screenshots saved after
/// ``start()`` count. Watching the Desktop asks the user for access once.
@MainActor
final class ScreenshotWatcher {
    /// What the history credits screenshots to; adding it to the ignored apps
    /// keeps them out.
    nonisolated static let sourceApp = SourceApp(id: "com.apple.screencaptureui", name: "Screenshot")

    /// The extended attribute macOS sets on screenshot files.
    nonisolated static let markerAttribute = "com.apple.metadata:kMDItemIsScreenCapture"

    /// The screenshots that were kept, oldest first. Finishes when the watcher stops.
    let changes: AsyncStream<PasteboardChange>

    nonisolated private static let logger = Logger(subsystem: "io.github.arash7l3.Clipo", category: "capture")

    private let folder: URL
    private let startedAt = Date()
    private let continuation: AsyncStream<PasteboardChange>.Continuation
    private let shouldRead: @MainActor (SourceApp?) -> Bool
    private var source: DispatchSourceFileSystemObject?
    private var seen = Set<String>()

    /// - Parameters:
    ///   - folder: Where screenshots are saved.
    ///   - shouldRead: Asked before a screenshot is read; `false` drops it
    ///     unread, for private mode and the ignored apps.
    init(folder: URL = ScreenshotWatcher.screenshotFolder(), shouldRead: @escaping @MainActor (SourceApp?) -> Bool) {
        self.folder = folder
        self.shouldRead = shouldRead
        let stream = AsyncStream.makeStream(of: PasteboardChange.self)
        changes = stream.stream
        continuation = stream.continuation
    }

    /// The folder the Screenshot app saves to: its Save To setting, or the Desktop.
    nonisolated static func screenshotFolder(
        settings: UserDefaults? = UserDefaults(suiteName: "com.apple.screencapture")
    ) -> URL {
        if let location = settings?.string(forKey: "location"), !location.isEmpty {
            return URL(fileURLWithPath: (location as NSString).expandingTildeInPath, isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Desktop", isDirectory: true)
    }

    /// Starts watching. Does nothing if already started.
    func start() {
        guard source == nil else { return }
        let descriptor = open(folder.path, O_EVTONLY)
        guard descriptor >= 0 else {
            // Most often the user declined access to the folder.
            Self.logger.error("the screenshot folder cannot be watched: errno \(errno, privacy: .public)")
            return
        }
        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: descriptor,
            eventMask: .write,
            queue: .main
        )
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.folderChanged() }
        }
        source.setCancelHandler { close(descriptor) }
        source.resume()
        self.source = source
    }

    /// Stops watching and ends ``changes``.
    func stop() {
        source?.cancel()
        source = nil
        continuation.finish()
    }

    /// Looks for new screenshots now and once more a moment later: the
    /// Screenshot app may mark a file just after it appears.
    private func folderChanged() {
        scan()
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(1.5))
            self?.scan()
        }
    }

    func scan() {
        for file in Self.screenshots(in: folder, since: startedAt) where seen.insert(file.path).inserted {
            guard let format = Self.format(ofFileAt: file.path), shouldRead(Self.sourceApp) else { continue }
            let continuation = continuation
            Task.detached(priority: .utility) {
                guard let data = try? Data(contentsOf: file, options: .mappedIfSafe) else {
                    Self.logger.error("a new screenshot could not be read")
                    return
                }
                let change = PasteboardChange(content: .image(data, format), sourceApp: Self.sourceApp, date: Date())
                continuation.yield(change)
            }
        }
    }

    /// The screenshot files in `folder` created after `date`, oldest first.
    nonisolated static func screenshots(in folder: URL, since date: Date) -> [URL] {
        let keys: Set<URLResourceKey> = [.creationDateKey, .isRegularFileKey]
        let entries = (try? FileManager.default.contentsOfDirectory(
            at: folder,
            includingPropertiesForKeys: Array(keys),
            options: [.skipsHiddenFiles]
        )) ?? []
        let found = entries.compactMap { url -> (url: URL, created: Date)? in
            guard let values = try? url.resourceValues(forKeys: keys),
                  values.isRegularFile == true,
                  let created = values.creationDate,
                  created > date,
                  isScreenshot(url)
            else { return nil }
            return (url, created)
        }
        return found.sorted { $0.created < $1.created }.map(\.url)
    }

    /// Whether macOS marked the file as a screenshot.
    nonisolated static func isScreenshot(_ url: URL) -> Bool {
        getxattr(url.path, markerAttribute, nil, 0, 0, 0) >= 0
    }

    /// The image format of a screenshot by its extension, or `nil` for one
    /// Clipo does not keep (HEIC, PDF).
    nonisolated static func format(ofFileAt path: String) -> ImageFormat? {
        switch URL(fileURLWithPath: path).pathExtension.lowercased() {
        case "png": .png
        case "jpg", "jpeg": .jpeg
        case "tif", "tiff": .tiff
        case "bmp": .bmp
        default: nil
        }
    }
}
