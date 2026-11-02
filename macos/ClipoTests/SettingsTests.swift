import AppKit
@testable import Clipo
import ClipoCore
import Foundation
import Testing

private func uniqueSuite() -> String {
    "io.github.arash7l3.Clipo.tests.\(UUID().uuidString)"
}

private func freshDefaults(suite: String = uniqueSuite()) throws -> UserDefaults {
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
        let suite = uniqueSuite()
        let defaults = try freshDefaults(suite: suite)
        InterfaceSettings.applyLanguage(.farsi, defaults: defaults)
        #expect(defaults.stringArray(forKey: InterfaceSettings.appleLanguagesKey) == ["fa"])

        InterfaceSettings.applyLanguage(.english, defaults: defaults)
        #expect(defaults.stringArray(forKey: InterfaceSettings.appleLanguagesKey) == ["en"])

        InterfaceSettings.applyLanguage(.system, defaults: defaults)
        // A suite also reads the system-wide languages; look at its own domain.
        let own = defaults.persistentDomain(forName: suite)
        #expect(own?[InterfaceSettings.appleLanguagesKey] == nil)
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
        #expect(HistorySettings.autoClearAfter(defaults: defaults) == TimeInterval(7 * 86_400))
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

struct SettingsTransferTests {
    @Test func everySettingRoundTrips() throws {
        let source = try freshDefaults()
        source.set(500, forKey: HistorySettings.maxItemsKey)
        source.set(7, forKey: HistorySettings.autoClearDaysKey)
        source.set(true, forKey: CaptureSettings.privateModeKey)
        source.set(true, forKey: PasteSettings.autoPasteKey)
        source.set(AppAppearance.dark.rawValue, forKey: InterfaceSettings.appearanceKey)
        source.set(AppLanguage.farsi.rawValue, forKey: InterfaceSettings.languageKey)
        source.set(StartTab.lastUsed.rawValue, forKey: PopupSettings.startTabKey)
        source.set(HotkeyChoice.controlOption.rawValue, forKey: InterfaceSettings.hotkeyKey)
        source.set(["com.example.vault"], forKey: CaptureSettings.ignoredAppsKey)

        let target = try freshDefaults()
        SettingsTransfer.apply(SettingsTransfer.export(defaults: source), defaults: target)

        #expect(SettingsTransfer.export(defaults: target) == SettingsTransfer.export(defaults: source))
        #expect(HistorySettings.maxItems(defaults: target) == 500)
        #expect(PopupSettings.startTab(defaults: target) == .lastUsed)
        #expect(InterfaceSettings.hotkey(defaults: target) == .controlOption)
        #expect(target.stringArray(forKey: InterfaceSettings.appleLanguagesKey) == ["fa"])
    }

    @Test func sharedKeysUseTheWindowsValues() throws {
        let defaults = try freshDefaults()
        defaults.set(AppLanguage.farsi.rawValue, forKey: InterfaceSettings.languageKey)
        let exported = Dictionary(
            uniqueKeysWithValues: SettingsTransfer.export(defaults: defaults).map { ($0.key, $0.value) }
        )

        #expect(exported[SettingsTransfer.languageKey] == "fa")
        #expect(exported[SettingsTransfer.appearanceKey] == "system")
        #expect(exported[SettingsTransfer.maxItemsKey] == "200")
        #expect(exported[SettingsTransfer.privateModeKey] == "false")
    }

    @Test func unknownKeysAndUnreadableValuesChangeNothing() throws {
        let defaults = try freshDefaults()
        defaults.set(AppAppearance.dark.rawValue, forKey: InterfaceSettings.appearanceKey)
        let odd = [
            ClipoCore.Setting(key: SettingsTransfer.maxItemsKey, value: "0"),
            ClipoCore.Setting(key: SettingsTransfer.appearanceKey, value: "sepia"),
            ClipoCore.Setting(key: SettingsTransfer.privateModeKey, value: "yes"),
            ClipoCore.Setting(key: "windows.hotkey", value: "ControlAlt"),
            ClipoCore.Setting(key: "windows.ignoredApps", value: "Vault.exe")
        ]

        SettingsTransfer.apply(odd, defaults: defaults)

        #expect(defaults.object(forKey: HistorySettings.maxItemsKey) == nil)
        #expect(InterfaceSettings.appearance(defaults: defaults) == .dark)
        #expect(defaults.object(forKey: CaptureSettings.privateModeKey) == nil)
        #expect(defaults.object(forKey: InterfaceSettings.hotkeyKey) == nil)
        #expect(CaptureSettings.ownIgnoredApps(defaults: defaults).isEmpty)
    }

    @Test func importedIgnoredAppsDropBlanksRepeatsAndBuiltIns() throws {
        let defaults = try freshDefaults()
        let apps = "com.example.a\n\nCOM.EXAMPLE.A\ncom.bitwarden.desktop\n com.example.b "

        let imported = ClipoCore.Setting(key: SettingsTransfer.ignoredAppsKey, value: apps)
        SettingsTransfer.apply([imported], defaults: defaults)

        #expect(CaptureSettings.ownIgnoredApps(defaults: defaults) == ["com.example.a", "com.example.b"])
    }
}
