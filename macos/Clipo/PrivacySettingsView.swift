import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// Private mode and the apps whose copies are never stored.
struct PrivacySettingsView: View {
    @AppStorage(CaptureSettings.privateModeKey) private var privateMode = false
    @State private var ownApps = CaptureSettings.ownIgnoredApps()
    @State private var newApp = ""

    var body: some View {
        Form {
            Section {
                Toggle("Private mode", isOn: $privateMode)
                Text("While on, nothing you copy is stored. Existing history is kept.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Section {
                ForEach(ownApps, id: \.self) { app in
                    HStack {
                        Text(verbatim: app)
                        Spacer()
                        Button("Remove") { remove(app) }
                    }
                }
                HStack {
                    TextField("Bundle identifier", text: $newApp)
                        .onSubmit { add(newApp) }
                    Button("Add") { add(newApp) }
                        .disabled(newApp.trimmingCharacters(in: .whitespaces).isEmpty)
                    Button("Choose App…") { chooseApp() }
                }
            } header: {
                Text("Never store copies from these apps")
            } footer: {
                Text("Password managers such as 1Password and Bitwarden are always ignored.")
            }
        }
        .formStyle(.grouped)
    }

    private func add(_ identifier: String) {
        if CaptureSettings.addIgnoredApp(identifier) {
            newApp = ""
        }
        ownApps = CaptureSettings.ownIgnoredApps()
    }

    private func remove(_ identifier: String) {
        CaptureSettings.removeIgnoredApp(identifier)
        ownApps = CaptureSettings.ownIgnoredApps()
    }

    private func chooseApp() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.applicationBundle]
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK,
              let url = panel.url,
              let identifier = Bundle(url: url)?.bundleIdentifier
        else { return }
        add(identifier)
    }
}
