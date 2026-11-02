import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// Snippets and export / import against a real store in a temporary folder.
@MainActor
final class ExtrasModelTests {
    private let folder: URL
    private let suite = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"

    init() throws {
        folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-extras-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    deinit {
        try? FileManager.default.removeItem(at: folder)
        for name in ["a", "source", "target"] {
            UserDefaults.standard.removePersistentDomain(forName: "\(suite).\(name)")
        }
    }

    /// A model on its own history and its own settings, never the app's.
    private func openModel(
        _ name: String = "a",
        defaults: UserDefaults? = nil
    ) async throws -> (ExtrasModel, ClipStore) {
        let url = try History.databaseURL(in: folder.appendingPathComponent(name, isDirectory: true))
        guard case let .ready(store, _) = History.open(at: url, keyStore: MemoryKeyStore()) else {
            throw CocoaError(.fileReadUnknown)
        }
        let model = ExtrasModel(defaults: try defaults ?? settings(name))
        await model.attach(store)
        return (model, store)
    }

    @Test func snippetsAreAddedChangedAndDeleted() async throws {
        let (model, _) = try await openModel()
        #expect(model.isAvailable)

        #expect(await model.save(id: nil, title: "Greeting", text: "Hello"))
        #expect(model.snippets.map(\.title) == ["Greeting"])

        let id = try #require(model.snippets.first?.id)
        #expect(await model.save(id: id, title: "Greeting", text: "Hi there"))
        #expect(model.snippets.first?.body == "Hi there")

        await model.delete(id)
        #expect(model.snippets.isEmpty)
    }

    @Test func blankSnippetsAreRefused() async throws {
        let (model, _) = try await openModel()

        #expect(await model.save(id: nil, title: "  ", text: "x") == false)
        #expect(await model.save(id: nil, title: "t", text: "") == false)
        #expect(model.snippets.isEmpty)
    }

    @Test func changeCallbackFires() async throws {
        let (model, _) = try await openModel()
        var changes = 0
        model.onSnippetsChange = { changes += 1 }

        _ = await model.save(id: nil, title: "a", text: "b")

        #expect(changes == 1)
    }

    @Test func exportCarriesHistoryAndSnippetsToAnotherStore() async throws {
        let (source, store) = try await openModel("source")
        _ = try store.record(capture: Capture(content: .text(text: TextContent(plain: "copied")), capturedAt: Date()))
        _ = await source.save(id: nil, title: "Greeting", text: "Hello")
        let data = try #require(await source.exportData())

        let (target, targetStore) = try await openModel("target")
        let result = await target.importData(data)

        guard case let .imported(summary) = result else {
            Issue.record("the export imports")
            return
        }
        #expect(summary.itemsAdded == 1)
        #expect(summary.snippetsAdded == 1)
        #expect(target.snippets.map(\.title) == ["Greeting"])
        #expect(try targetStore.count() == 1)
    }

    @Test func exportFileIsTheUsersAloneAndReplacesAnOldOne() throws {
        let url = folder.appendingPathComponent("Clipo Export.clipoexport")
        try Data("an older, longer export".utf8).write(to: url)

        try DataSettingsView.writePrivately(Data("new".utf8), to: url)

        #expect(try Data(contentsOf: url) == Data("new".utf8))
        let attributes = try FileManager.default.attributesOfItem(atPath: url.path)
        #expect((attributes[.posixPermissions] as? Int) == 0o600)
    }

    /// Settings of their own for the model called `name`.
    private func settings(_ name: String) throws -> UserDefaults {
        let defaults = try #require(UserDefaults(suiteName: "\(suite).\(name)"))
        defaults.removePersistentDomain(forName: "\(suite).\(name)")
        return defaults
    }

    @Test func exportCarriesItemNamesAndSettings() async throws {
        let sourceDefaults = try settings("source")
        sourceDefaults.set(500, forKey: HistorySettings.maxItemsKey)
        sourceDefaults.set(AppAppearance.dark.rawValue, forKey: InterfaceSettings.appearanceKey)
        sourceDefaults.set(["com.example.vault"], forKey: CaptureSettings.ignoredAppsKey)
        let (source, store) = try await openModel("source", defaults: sourceDefaults)
        let copied = Capture(content: .text(text: TextContent(plain: "copied")), capturedAt: Date())
        guard case let .added(item) = try store.record(capture: copied) else {
            Issue.record("the copy is added")
            return
        }
        try store.setTitle(id: item.id, title: "Named")
        let data = try #require(await source.exportData())

        let targetDefaults = try settings("target")
        let (target, targetStore) = try await openModel("target", defaults: targetDefaults)
        _ = await target.importData(data)

        let previews = try targetStore.previews(query: Query(category: .all, text: ""))
        #expect(previews.first?.title == "Named")
        #expect(HistorySettings.maxItems(defaults: targetDefaults) == 500)
        #expect(InterfaceSettings.appearance(defaults: targetDefaults) == .dark)
        #expect(CaptureSettings.ownIgnoredApps(defaults: targetDefaults) == ["com.example.vault"])
    }

    @Test func garbageIsNotAnExport() async throws {
        let (model, _) = try await openModel()

        #expect(await model.importData(Data("not an export".utf8)) == .invalid)
    }
}
