import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

/// Snippets and export / import against a real store in a temporary folder.
@MainActor
final class ExtrasModelTests {
    private let folder: URL

    init() throws {
        folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-extras-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    deinit {
        try? FileManager.default.removeItem(at: folder)
    }

    private func openModel(_ name: String = "a") async throws -> (ExtrasModel, ClipStore) {
        let url = try History.databaseURL(in: folder.appendingPathComponent(name, isDirectory: true))
        guard case let .ready(store, _) = History.open(at: url, keyStore: MemoryKeyStore()) else {
            throw CocoaError(.fileReadUnknown)
        }
        let model = ExtrasModel()
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

    @Test func garbageIsNotAnExport() async throws {
        let (model, _) = try await openModel()

        #expect(await model.importData(Data("not an export".utf8)) == .invalid)
    }
}
