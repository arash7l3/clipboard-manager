import AppKit
import ClipoCore

/// One clipboard change, ready to be turned into a capture off the main thread.
struct PasteboardChange: Sendable {
    let content: PasteboardContent
    let sourceApp: SourceApp?
    let date: Date
}

/// Notices clipboard changes by polling the pasteboard's change count, because
/// macOS has no change notification.
///
/// Changes arrive in order on ``changes``. Whatever is on the pasteboard when
/// ``start()`` is called is not captured: it may come from an app that is
/// ignored, and only a new copy tells us which app that was.
@MainActor
final class PasteboardWatcher {
    /// How often the change count is read. Cheap: no data is touched until it moves.
    static let interval: TimeInterval = 0.3

    /// The changes that were kept, oldest first. Finishes when the watcher stops.
    let changes: AsyncStream<PasteboardChange>

    private let pasteboard: NSPasteboard
    private let continuation: AsyncStream<PasteboardChange>.Continuation
    private let frontmostApp: @MainActor () -> SourceApp?
    private let shouldRead: @MainActor (SourceApp?) -> Bool
    private var lastChangeCount: Int
    private var timer: Timer?

    /// - Parameters:
    ///   - pasteboard: What to watch.
    ///   - frontmostApp: The app to credit a change to. The pasteboard does not
    ///     say who wrote to it; the frontmost app is right unless the user
    ///     switched apps within one polling interval of copying.
    ///   - shouldRead: Asked before any data is read; `false` drops the change
    ///     unread, for private mode and ignored apps.
    init(
        pasteboard: NSPasteboard = .general,
        frontmostApp: @escaping @MainActor () -> SourceApp? = PasteboardWatcher.currentFrontmostApp,
        shouldRead: @escaping @MainActor (SourceApp?) -> Bool
    ) {
        self.pasteboard = pasteboard
        self.frontmostApp = frontmostApp
        self.shouldRead = shouldRead
        lastChangeCount = pasteboard.changeCount
        let stream = AsyncStream.makeStream(of: PasteboardChange.self)
        changes = stream.stream
        continuation = stream.continuation
    }

    /// The frontmost application, by bundle identifier. Apps without one
    /// (rare command-line tools) cannot be matched by the ignore list.
    static func currentFrontmostApp() -> SourceApp? {
        guard let app = NSWorkspace.shared.frontmostApplication, let id = app.bundleIdentifier else {
            return nil
        }
        return SourceApp(id: id, name: app.localizedName)
    }

    /// Starts polling. Does nothing if already started.
    func start() {
        guard timer == nil else { return }
        lastChangeCount = pasteboard.changeCount
        let timer = Timer(timeInterval: Self.interval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.poll() }
        }
        // Slack lets the system batch wake-ups. The common modes keep the
        // timer running while a menu is open.
        timer.tolerance = Self.interval / 3
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    /// Stops polling and ends ``changes``.
    func stop() {
        timer?.invalidate()
        timer = nil
        continuation.finish()
    }

    /// Treats what is on the pasteboard now as seen. Called right after Clipo
    /// itself wrote an item back, which must not count as a new copy.
    func skipCurrent() {
        lastChangeCount = pasteboard.changeCount
    }

    /// Reads the pasteboard if it changed since the last call.
    func poll() {
        let count = pasteboard.changeCount
        guard count != lastChangeCount else { return }
        lastChangeCount = count

        let app = frontmostApp()
        guard shouldRead(app), let content = PasteboardReader.content(of: pasteboard) else { return }
        continuation.yield(PasteboardChange(content: content, sourceApp: app, date: Date()))
    }
}
