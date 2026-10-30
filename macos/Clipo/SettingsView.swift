import SwiftUI

/// The Settings window's content: one tab per topic.
struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralSettingsView()
                .tabItem { Label("General", systemImage: "gearshape") }
            HistorySettingsView()
                .tabItem { Label("History", systemImage: "clock.arrow.circlepath") }
            PrivacySettingsView()
                .tabItem { Label("Privacy", systemImage: "hand.raised") }
        }
        .frame(width: 500, height: 420)
    }
}

/// Start at login, language, appearance, shortcut and auto-paste.
struct GeneralSettingsView: View {
    @AppStorage(InterfaceSettings.appearanceKey) private var appearance = AppAppearance.system
    @AppStorage(InterfaceSettings.languageKey) private var language = AppLanguage.system
    @AppStorage(InterfaceSettings.hotkeyKey) private var hotkey = HotkeyChoice.commandShift
    @AppStorage(PasteSettings.autoPasteKey) private var autoPaste = false
    @State private var startAtLogin = LoginItem.isOn
    @State private var needsApproval = LoginItem.needsApproval
    @State private var loginFailed = false

    var body: some View {
        Form {
            Section {
                Toggle("Start Clipo at login", isOn: $startAtLogin)
                    .onChange(of: startAtLogin) { _, wanted in setLogin(wanted) }
                loginNotes
            }
            Section {
                Picker("Appearance", selection: $appearance) {
                    ForEach(AppAppearance.allCases) { Text($0.title).tag($0) }
                }
                languagePicker
            }
            Section {
                Picker("Open history with", selection: $hotkey) {
                    ForEach(HotkeyChoice.allCases) { Text(verbatim: $0.label).tag($0) }
                }
                Toggle("Paste automatically after choosing an item", isOn: $autoPaste)
                    .onChange(of: autoPaste) { _, wanted in askForAccessibility(if: wanted) }
                Text("Needs the Accessibility permission. Off, an item is only put back on the clipboard.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onAppear { refreshLogin() }
    }

    @ViewBuilder private var loginNotes: some View {
        if loginFailed {
            Text("macOS did not let Clipo change its login item.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        if needsApproval {
            Text("Allow Clipo in System Settings > General > Login Items.")
                .font(.caption)
                .foregroundStyle(.secondary)
            Button("Open Login Items Settings") { LoginItem.openSystemSettings() }
        }
    }

    @ViewBuilder private var languagePicker: some View {
        Picker("Language", selection: $language) {
            Text("Match System").tag(AppLanguage.system)
            Text(verbatim: "English").tag(AppLanguage.english)
            Text(verbatim: "فارسی").tag(AppLanguage.farsi)
        }
        .onChange(of: language) { _, chosen in InterfaceSettings.applyLanguage(chosen) }
        if language != InterfaceSettings.launchLanguage {
            HStack {
                Text("Restart Clipo to change the language.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Restart") { InterfaceSettings.relaunch() }
            }
        }
    }

    private func setLogin(_ wanted: Bool) {
        guard wanted != LoginItem.isOn else { return }
        loginFailed = !LoginItem.setOn(wanted)
        refreshLogin()
    }

    private func refreshLogin() {
        startAtLogin = LoginItem.isOn
        needsApproval = LoginItem.needsApproval
    }

    /// Without the permission auto-paste does nothing, so ask when it is
    /// turned on instead of at the first paste.
    private func askForAccessibility(if wanted: Bool) {
        if wanted, !Accessibility.isGranted {
            Accessibility.explain()
        }
    }
}

/// How many items are kept and for how long.
struct HistorySettingsView: View {
    @AppStorage(HistorySettings.maxItemsKey) private var maxItems = HistorySettings.defaultMaxItems
    @AppStorage(HistorySettings.autoClearDaysKey) private var autoClearDays = 0

    var body: some View {
        Form {
            Section {
                Picker("Items to keep", selection: $maxItems) {
                    ForEach(HistorySettings.maxItemsChoices, id: \.self) { count in
                        Text(count, format: .number).tag(count)
                    }
                }
                Text("Pinned items are never removed to make room.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Section {
                Picker("Remove unpinned items", selection: $autoClearDays) {
                    Text("Never").tag(0)
                    Text("After 1 day").tag(1)
                    Text("After 7 days").tag(7)
                    Text("After 30 days").tag(30)
                }
            }
        }
        .formStyle(.grouped)
    }
}
