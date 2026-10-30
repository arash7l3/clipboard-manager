@testable import Clipo
import ClipoCore
import Foundation
import Testing

struct CaptureSettingsTests {
    private func defaults() throws -> UserDefaults {
        let suite = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defaults.removePersistentDomain(forName: suite)
        return defaults
    }

    @Test func passwordManagersAreIgnoredByDefault() throws {
        let rules = CaptureSettings.rules(defaults: try defaults())
        #expect(rules.ignoredApps.contains("com.1password.1password"))
        #expect(rules.ignoredApps.contains("com.bitwarden.desktop"))
        #expect(!rules.privateMode)
        #expect(rules.autoClearAfter == nil)
    }

    @Test func userEntriesAreAddedWithoutRepeats() throws {
        let defaults = try defaults()
        defaults.set(["com.example.secrets", "COM.BITWARDEN.DESKTOP"], forKey: CaptureSettings.ignoredAppsKey)

        let apps = CaptureSettings.ignoredApps(defaults: defaults)
        #expect(apps.contains("com.example.secrets"))
        #expect(apps.filter { $0.lowercased() == "com.bitwarden.desktop" }.count == 1)
        #expect(Set(apps.map { $0.lowercased() }).count == apps.count)
    }

    @Test func readingIsRefusedInPrivateModeAndForIgnoredApps() {
        var rules = CaptureRules(privateMode: false, ignoredApps: ["com.Example.Secrets"], autoClearAfter: nil)
        let secrets = SourceApp(id: "com.example.secrets", name: nil)
        let editor = SourceApp(id: "com.example.editor", name: nil)

        #expect(!CaptureSettings.allowsReading(from: secrets, under: rules))
        #expect(CaptureSettings.allowsReading(from: editor, under: rules))
        #expect(CaptureSettings.allowsReading(from: nil, under: rules))

        rules.privateMode = true
        #expect(!CaptureSettings.allowsReading(from: editor, under: rules))
    }
}

final class ClipboardRecorderTests {
    private let folder: URL

    init() throws {
        folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-tests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    deinit {
        try? FileManager.default.removeItem(at: folder)
    }

    private func openStore() throws -> ClipStore {
        let url = try History.databaseURL(in: folder)
        guard case let .ready(store, _) = History.open(at: url, keyStore: MemoryKeyStore()) else {
            throw ClipoError.Storage(message: "test store did not open")
        }
        try store.setRules(rules: CaptureRules(
            privateMode: false,
            ignoredApps: CaptureSettings.defaultIgnoredApps,
            autoClearAfter: nil
        ))
        return store
    }

    private func change(_ text: String, app: String? = nil) -> PasteboardChange {
        PasteboardChange(
            content: .text(text, rtf: nil, html: nil),
            sourceApp: app.map { SourceApp(id: $0, name: nil) },
            date: Date()
        )
    }

    @Test func storesNewTextAndBumpsARepeat() throws {
        let store = try openStore()

        let first = ClipboardRecorder.record(change("سلام", app: "com.example.editor"), in: store)
        guard case let .added(item)? = first else {
            Issue.record("new text is added")
            return
        }
        #expect(item.sourceApp?.id == "com.example.editor")

        guard case .bumped? = ClipboardRecorder.record(change("سلام"), in: store) else {
            Issue.record("a repeat is bumped")
            return
        }
        #expect(try store.count() == 1)
    }

    @Test func ignoredAppsAreSkippedByTheCore() throws {
        let store = try openStore()

        let result = ClipboardRecorder.record(change("secret", app: "com.1password.1password"), in: store)
        #expect(result == .skipped(reason: .ignoredApp))
        #expect(try store.count() == 0)
    }

    @Test func privateModeSkipsEverything() throws {
        let store = try openStore()
        store.setPrivateMode(on: true)

        #expect(ClipboardRecorder.record(change("anything"), in: store) == .skipped(reason: .privateMode))
        #expect(try store.count() == 0)
    }
}
