import AppKit
import DollyPasteCore

final class SettingsWindow: NSWindow {}

final class SettingsController: NSWindowController, NSWindowDelegate {
    private let model: AppModel
    private let retention = NSTextField()
    private let maximum = NSTextField()
    private let persistence = NSButton(checkboxWithTitle: "Save history between launches", target: nil, action: nil)
    private let errorLabel = label("", size: 11, color: .systemRed)

    init(model: AppModel) {
        self.model = model
        let window = SettingsWindow(contentRect: NSRect(x: 0, y: 0, width: 440, height: 350),
                                    styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "Dolly Paste Settings"
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.appearance = NSAppearance(named: .aqua)
        window.delegate = self
        buildInterface()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    private func buildInterface() {
        guard let root = window?.contentView else { return }
        let heading = label("Clipboard history", size: 17, weight: .semibold)
        retention.placeholderString = "1440"
        maximum.placeholderString = "200"
        retention.setAccessibilityLabel("Retain clipboard history for minutes")
        maximum.setAccessibilityLabel("Maximum number of stored clips")
        for field in [retention, maximum] {
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(equalToConstant: 96).isActive = true
            field.alignment = .right
        }
        let rows = NSGridView(views: [
            [label("Keep for 1–43,200 minutes"), retention],
            [label("Maximum clips (10–1,000)"), maximum]
        ])
        rows.rowSpacing = 10
        rows.columnSpacing = 16
        rows.xPlacement = .leading
        rows.yPlacement = .center
        rows.translatesAutoresizingMaskIntoConstraints = false
        persistence.setAccessibilityLabel("Save clipboard history on this Mac between launches")
        let noteText = model.isDemo
            ? "Demo settings apply only to synthetic clips. Pinned clips are still subject to age and count limits."
            : "Pinned clips are still subject to age and count limits. Local history is encrypted with a key stored in your macOS login Keychain."
        let note = label(noteText, size: 11, color: .secondaryLabelColor)
        note.maximumNumberOfLines = 0
        note.lineBreakMode = .byWordWrapping
        let destination = label("Dolly Paste cannot protect pasted content based on the destination app. Pause capture when copying sensitive information.", size: 11, color: .secondaryLabelColor)
        destination.maximumNumberOfLines = 0
        destination.lineBreakMode = .byWordWrapping
        let cancel = ActionButton("Cancel") { [weak self] in self?.close() }
        cancel.keyEquivalent = "\u{1b}"
        let save = ActionButton("Save") { [weak self] in self?.save() }
        save.keyEquivalent = "\r"
        let buttons = horizontalStack([flexibleSpace(), cancel, save])
        let stack = NSStackView(views: [heading, rows, persistence, note, destination, errorLabel, flexibleSpace(), buttons])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 11
        stack.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 20),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -20),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 18),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            note.widthAnchor.constraint(equalTo: stack.widthAnchor),
            destination.widthAnchor.constraint(equalTo: stack.widthAnchor),
            buttons.widthAnchor.constraint(equalTo: stack.widthAnchor)
        ])
        errorLabel.isHidden = true
        errorLabel.maximumNumberOfLines = 2
        errorLabel.lineBreakMode = .byWordWrapping
        errorLabel.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
    }

    func show() {
        retention.integerValue = model.history.settings.maxAgeMinutes
        maximum.integerValue = model.history.settings.maxItems
        persistence.state = model.history.settings.persistHistory ? .on : .off
        persistence.isEnabled = !model.isDemo
        errorLabel.isHidden = true
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
    }

    private func save() {
        guard let age = Int(retention.stringValue), (1...43200).contains(age),
              let count = Int(maximum.stringValue), (10...1000).contains(count) else {
            errorLabel.stringValue = "Use 1–43,200 minutes and 10–1,000 clips, in whole numbers."
            errorLabel.isHidden = false
            return
        }
        var settings = model.history.settings
        settings.maxAgeMinutes = age
        settings.maxItems = count
        settings.persistHistory = persistence.state == .on
        if model.applySettings(settings) {
            close()
        } else {
            errorLabel.stringValue = model.warning ?? "Could not save settings. Please try again."
            errorLabel.toolTip = errorLabel.stringValue
            errorLabel.isHidden = false
        }
    }
}
