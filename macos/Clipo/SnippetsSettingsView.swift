import ClipoCore
import SwiftUI

/// Saved text templates: add, change and delete. They are pasted from the
/// Snippets submenu of the menu-bar icon.
struct SnippetsSettingsView: View {
    let model: ExtrasModel
    @State private var editing: Int64?
    @State private var title = ""
    @State private var text = ""
    @State private var failed = false

    var body: some View {
        Form {
            Section {
                if model.snippets.isEmpty {
                    Text("No snippets yet.")
                        .foregroundStyle(.secondary)
                }
                ForEach(model.snippets, id: \.id) { snippet in
                    HStack {
                        VStack(alignment: .leading) {
                            Text(verbatim: snippet.title)
                            Text(verbatim: PopupText.firstLine(snippet.body))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                        }
                        Spacer()
                        Button("Edit") { edit(snippet) }
                        Button("Delete") { delete(snippet) }
                    }
                }
            }
            Section {
                TextField("Title", text: $title)
                TextEditor(text: $text)
                    .frame(height: 70)
                    .accessibilityLabel(Text("Snippet text"))
                if failed {
                    Text("The snippet needs a title and some text.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                HStack {
                    Spacer()
                    if editing != nil {
                        Button("Cancel") { clear() }
                    }
                    Group {
                        if editing == nil {
                            Button("Add Snippet") { save() }
                        } else {
                            Button("Save Snippet") { save() }
                        }
                    }
                    .disabled(title.trimmingCharacters(in: .whitespaces).isEmpty || text.isEmpty)
                }
            }
        }
        .formStyle(.grouped)
        .disabled(!model.isAvailable)
    }

    private func edit(_ snippet: Snippet) {
        editing = snippet.id
        title = snippet.title
        text = snippet.body
        failed = false
    }

    private func delete(_ snippet: Snippet) {
        if editing == snippet.id {
            clear()
        }
        Task { await model.delete(snippet.id) }
    }

    private func save() {
        Task {
            if await model.save(id: editing, title: title, text: text) {
                clear()
            } else {
                failed = true
            }
        }
    }

    private func clear() {
        editing = nil
        title = ""
        text = ""
        failed = false
    }
}
