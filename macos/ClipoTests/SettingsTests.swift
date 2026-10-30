import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

private func freshDefaults() throws -> UserDefaults {
    let suite = "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
    let defaults = try #require(UserDefaults(suiteName: suite))
    defaults.removePersistentDomain(forName: suite)
    return defaults
}

struct InterfaceSettingsTests {
    @Test func defaultsFollowTheSystemAndDecision0004() throws {
        let defaults = try freshDefaults()
        #expect(InterfaceSettings.appearance(defaults: defaults) == .system)
        #expect(InterfaceSettings.language(defaults: defaults) == .system)
        #expect(InterfaceSettings.hotkey(defaults: defaults) == .commandShift)
        #expect(HotkeyChoice.commandShift.combo == KeyCombo.showHistory)
        #expect(!PasteSettings.autoPaste(defaults: defaults))
    }

    @Test func unknownStoredValuesFallBack() throws {
        let defaults = try freshDefaults()
        defaults.set("sepia", forKey: InterfaceSettings.appearanceKey)
        defaults.set("tlh", forKey: InterfaceSettings.languageKey)
        defaults.set("hyper", forKey: InterfaceSettings.hotkeyKey)
        #expect(InterfaceSettings.appearance(defaults: defaults) == .system)
        #expect(InterfaceSettings.language(defaults: defaults) == .system)
        #expect(InterfaceSettings.hotkey(defaults: defaults) == .commandShift)
    }

    @Test func appearanceMapsToAppKit() {
        #expect(AppAppearance.system.nsAppearance == nil)
        #expect(AppAppearance.light.nsAppearance?.name == .aqua)
        #expect(AppAppearance.dark.nsAppearance?.name == .darkAqua)
    }

    @Test func languageIsAppliedThroughAppleLanguages() throws {
        let defaults = try freshDefaults()
        InterfaceSettings.applyLanguage(.farsi, defaults: defaults)
        #expect(defaults.stringArray(forKey: InterfaceSettings.appleLanguagesKey) == ["fa"])

        InterfaceSettings.applyLanguage(.english, defaults: defaults)
        #expect(defaults.stringArray(forKey: InterfaceSettings.appleLanguagesKey) == ["en"])

        InterfaceSettings.applyLanguage(.system, defaults: defaults)
        #expect(defaults.object(forKey: InterfaceSettings.appleLanguagesKey) == nil)
    }

    @Test func everyShortcutIsDistinctAndUsesTwoModifiers() {
        let combos = HotkeyChoice.allCases.map(\.combo)
        for (index, combo) in combos.enumerated() {
            #expect(combos.firstIndex(of: combo) == index)
            #expect(combo.carbonModifiers.nonzeroBitCount == 2)
        }
        #expect(HotkeyChoice.commandShift.label == "⇧⌘V")
        #expect(HotkeyChoice.controlOption.label == "⌃⌥V")
    }
}

struct HistorySettingsTests {
    @Test func historySizeDefaultsToTheCoresAndIsClamped() throws {
        #expect(HistorySettings.defaultMaxItems == Int(defaultHistoryLimits().maxItems))
        #expect(HistorySettings.maxItemsChoices.contains(HistorySettings.defaultMaxItems))

        let defaults = try freshDefaults()
        #expect(HistorySettings.maxItems(defaults: defaults) == 200)
        defaults.set(0, forKey: HistorySettings.maxItemsKey)
        #expect(HistorySettings.maxItems(defaults: defaults) == 1)
        defaults.set(5_000_000, forKey: HistorySettings.maxItemsKey)
        #expect(HistorySettings.maxItems(defaults: defaults) == 100_000)
    }

    @Test func autoClearIsOffUnlessDaysAreSet() throws {
        let defaults = try freshDefaults()
        #expect(HistorySettings.autoClearAfter(defaults: defaults) == nil)
        defaults.set(7, forKey: HistorySettings.autoClearDaysKey)
        #expect(HistorySettings.autoClearAfter(defaults: defaults) == 7 * 86_400)
        defaults.set(-3, forKey: HistorySettings.autoClearDaysKey)
        #expect(HistorySettings.autoClearAfter(defaults: defaults) == nil)
    }

    @Test func rulesCarryPrivateModeAndAutoClear() throws {
        let defaults = try freshDefaults()
        defaults.set(true, forKey: CaptureSettings.privateModeKey)
        defaults.set(1, forKey: HistorySettings.autoClearDaysKey)
        let rules = CaptureSettings.rules(defaults: defaults)
        #expect(rules.privateMode)
        #expect(rules.autoClearAfter == 86_400)
    }

    @Test func ignoredAppsAreAddedAndRemoved() throws {
        let defaults = try freshDefaults()
        #expect(CaptureSettings.addIgnoredApp("  com.example.Secrets ", defaults: defaults))
        #expect(CaptureSettings.ownIgnoredApps(defaults: defaults) == ["com.example.Secrets"])

        #expect(!CaptureSettings.addIgnoredApp("COM.EXAMPLE.SECRETS", defaults: defaults))
        #expect(!CaptureSettings.addIgnoredApp("com.1password.1password", defaults: defaults))
        #expect(!CaptureSettings.addIgnoredApp("   ", defaults: defaults))
        #expect(CaptureSettings.ownIgnoredApps(defaults: defaults).count == 1)

        CaptureSettings.removeIgnoredApp("com.example.secrets", defaults: defaults)
        #expect(CaptureSettings.ownIgnoredApps(defaults: defaults).isEmpty)
        #expect(CaptureSettings.ignoredApps(defaults: defaults).contains("com.1password.1password"))
    }

    @Test func snapshotsDifferOnlyWhenASettingChanged() throws {
        let defaults = try freshDefaults()
        let before = SettingsSnapshot(defaults: defaults)
        defaults.set("unrelated", forKey: "someOtherKey")
        #expect(SettingsSnapshot(defaults: defaults) == before)

        defaults.set(true, forKey: CaptureSettings.privateModeKey)
        let after = SettingsSnapshot(defaults: defaults)
        #expect(after != before)
        #expect(after.history.privateMode)
        #expect(after.appearance == before.appearance)
    }

    @Test func applyingTrimsTheOpenHistoryToTheLimit() throws {
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("clipo-tests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let url = try History.databaseURL(in: folder)
        guard case let .ready(store, _) = History.open(at: url, keyStore: MemoryKeyStore()) else {
            Issue.record("a new history opens")
            return
        }
        for text in ["one", "two", "three"] {
            let capture = Capture(content: .text(text: TextContent(plain: text)), capturedAt: Date())
            _ = try store.record(capture: capture)
        }
        #expect(try store.count() == 3)

        let defaults = try freshDefaults()
        defaults.set(50, forKey: HistorySettings.maxItemsKey)
        defaults.set(true, forKey: CaptureSettings.privateModeKey)
        #expect(HistorySettings.apply(to: store, defaults: defaults))
        #expect(store.limits().maxItems == 50)
        #expect(store.rules().privateMode)
        #expect(try store.count() == 3)

        // The smallest choice in Settings is above this, but the core honors any limit.
        defaults.set(1, forKey: HistorySettings.maxItemsKey)
        #expect(HistorySettings.apply(to: store, defaults: defaults))
        #expect(try store.count() == 1)
    }
}

/// Every string of the catalog must have a Farsi translation.
struct CatalogTests {
    @Test func everyStringIsTranslatedIntoFarsi() throws {
        let catalog = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("Clipo/Localizable.xcstrings")
        let json = try JSONSerialization.jsonObject(with: Data(contentsOf: catalog))
        let strings = try #require((json as? [String: Any])?["strings"] as? [String: [String: Any]])
        #expect(strings.count > 60)

        for (key, entry) in strings {
            let localizations = entry["localizations"] as? [String: Any]
            #expect(localizations?["fa"] != nil, "missing Farsi: \(key)")
        }
    }
}
