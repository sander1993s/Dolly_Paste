import AppKit
import DollyPasteCore

final class PickerController: NSWindowController, NSWindowDelegate, NSSearchFieldDelegate,
                              NSTableViewDataSource, NSTableViewDelegate {
    private let model: AppModel
    private let search = NSSearchField()
    private let scope = NSSegmentedControl(labels: ["All", "Pinned"], trackingMode: .selectOne, target: nil, action: nil)
    private let kinds = NSSegmentedControl(labels: ["All types", "Text", "Links", "Code", "Images"], trackingMode: .selectOne, target: nil, action: nil)
    private let table = PickerTableView()
    private let warningLabel = label("", size: 11, color: .secondaryLabelColor)
    private let countLabel = label("", size: 11, color: .secondaryLabelColor)
    private let preview = NSStackView()
    private let previewTitle = label("Preview", size: 11, weight: .semibold)
    private let previewText = NSTextView()
    private let previewImage = NSImageView()
    private let previewScroll = NSScrollView()
    private var copyButton: ActionButton!
    private var pinButton: ActionButton!
    private var deleteButton: ActionButton!
    private var previewButton: ActionButton!
    private var previewCopyText: ActionButton!
    private var previewCopyBase64: ActionButton!
    private var pauseButton: ActionButton!
    private var entries: [ClipEntry] = []
    private var selectedID: UUID?
    private var copyTarget: CopyTarget = .text
    private var previewExpanded = false
    private var updatingSelection = false
    private var presentingDialog = false
    private let blue = NSColor(calibratedRed: 0.02, green: 0.43, blue: 0.77, alpha: 1)

    init(model: AppModel) {
        self.model = model
        let window = PickerWindow(contentRect: NSRect(x: 0, y: 0, width: 480, height: 520),
                                  styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.title = "Dolly Paste"
        window.contentMinSize = NSSize(width: 420, height: 460)
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]
        window.appearance = NSAppearance(named: .aqua)
        window.delegate = self
        window.shortcutHandler = { [weak self] in self?.handleKey($0) ?? false }
        buildInterface()
        refresh()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    private func buildInterface() {
        guard let root = window?.contentView else { return }
        root.wantsLayer = true
        root.layer?.backgroundColor = NSColor.white.cgColor

        let header = NSView()
        header.wantsLayer = true
        header.layer?.backgroundColor = blue.cgColor
        header.translatesAutoresizingMaskIntoConstraints = false
        let sheep = SheepView()
        sheep.translatesAutoresizingMaskIntoConstraints = false
        sheep.setAccessibilityElement(false)
        let heading = horizontalStack([sheep, label("Dolly Paste", size: 20, weight: .semibold, color: .white), flexibleSpace()])
        search.placeholderString = "Search clipboard history…"
        search.font = .systemFont(ofSize: 13)
        search.delegate = self
        search.sendsSearchStringImmediately = true
        search.translatesAutoresizingMaskIntoConstraints = false
        search.setAccessibilityLabel("Search clipboard history")
        search.identifier = NSUserInterfaceItemIdentifier("history.search")
        scope.selectedSegment = 0
        scope.target = self
        scope.action = #selector(filtersChanged)
        scope.segmentStyle = .rounded
        scope.setAccessibilityLabel("Show all clips or pinned clips")
        scope.identifier = NSUserInterfaceItemIdentifier("history.scope")
        kinds.selectedSegment = 0
        kinds.target = self
        kinds.action = #selector(filtersChanged)
        kinds.segmentStyle = .rounded
        kinds.controlSize = .small
        kinds.setAccessibilityLabel("Filter by clipboard content type")
        kinds.identifier = NSUserInterfaceItemIdentifier("history.kinds")
        scope.translatesAutoresizingMaskIntoConstraints = false
        kinds.translatesAutoresizingMaskIntoConstraints = false
        header.addSubview(heading)
        header.addSubview(search)
        header.addSubview(scope)
        header.addSubview(kinds)

        NSLayoutConstraint.activate([
            header.heightAnchor.constraint(equalToConstant: 124),
            heading.topAnchor.constraint(equalTo: header.topAnchor, constant: 6),
            heading.leadingAnchor.constraint(equalTo: header.leadingAnchor, constant: 14),
            heading.trailingAnchor.constraint(equalTo: header.trailingAnchor, constant: -14),
            heading.heightAnchor.constraint(equalToConstant: 26),
            sheep.widthAnchor.constraint(equalToConstant: 30), sheep.heightAnchor.constraint(equalToConstant: 30),
            search.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: 4),
            search.leadingAnchor.constraint(equalTo: header.leadingAnchor, constant: 14),
            search.trailingAnchor.constraint(equalTo: header.trailingAnchor, constant: -14),
            search.heightAnchor.constraint(equalToConstant: 26),
            scope.topAnchor.constraint(equalTo: search.bottomAnchor, constant: 5),
            scope.leadingAnchor.constraint(equalTo: search.leadingAnchor),
            kinds.topAnchor.constraint(equalTo: scope.bottomAnchor, constant: 3),
            kinds.leadingAnchor.constraint(equalTo: search.leadingAnchor),
            kinds.trailingAnchor.constraint(lessThanOrEqualTo: search.trailingAnchor)
        ])

        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("Clip"))
        table.addTableColumn(column)
        table.headerView = nil
        table.dataSource = self
        table.delegate = self
        table.target = self
        table.shortcutHandler = { [weak self] in self?.handleKey($0) ?? false }
        table.doubleAction = #selector(copySelection)
        table.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
        table.autoresizingMask = [.width]
        table.selectionHighlightStyle = .regular
        table.style = .plain
        table.intercellSpacing = NSSize(width: 0, height: 1)
        table.backgroundColor = .white
        table.setAccessibilityLabel("Clipboard history")
        table.identifier = NSUserInterfaceItemIdentifier("history.list")
        let listScroll = NSScrollView()
        listScroll.documentView = table
        listScroll.hasVerticalScroller = true
        listScroll.autohidesScrollers = true
        listScroll.borderType = .noBorder
        listScroll.translatesAutoresizingMaskIntoConstraints = false

        warningLabel.lineBreakMode = .byWordWrapping
        warningLabel.maximumNumberOfLines = 2
        warningLabel.setContentCompressionResistancePriority(.required, for: .vertical)
        copyButton = ActionButton("Copy", accessibilityLabel: "Copy selected clip, Command C") { [weak self] in self?.copySelection() }
        pinButton = ActionButton("Pin", accessibilityLabel: "Pin or unpin selected clip, Command P") { [weak self] in self?.pinSelection() }
        deleteButton = ActionButton("Delete", accessibilityLabel: "Delete selected clip") { [weak self] in self?.deleteSelection() }
        previewButton = ActionButton("Preview", accessibilityLabel: "Toggle full preview, Command Space") { [weak self] in self?.togglePreview() }
        previewButton.toolTip = "Show or hide the preview (⌘Space or ⌘⇧Space). macOS may reserve ⌘Space for Spotlight."
        copyButton.identifier = NSUserInterfaceItemIdentifier("history.copy")
        pinButton.identifier = NSUserInterfaceItemIdentifier("history.pin")
        deleteButton.identifier = NSUserInterfaceItemIdentifier("history.delete")
        previewButton.identifier = NSUserInterfaceItemIdentifier("history.preview-toggle")
        let actions = horizontalStack([copyButton, pinButton, deleteButton, flexibleSpace(), previewButton])

        preview.orientation = .vertical
        preview.alignment = .leading
        preview.spacing = 4
        preview.translatesAutoresizingMaskIntoConstraints = false
        preview.identifier = NSUserInterfaceItemIdentifier("history.preview")
        preview.distribution = .fill
        preview.wantsLayer = true
        preview.layer?.backgroundColor = NSColor(calibratedWhite: 0.97, alpha: 1).cgColor
        preview.edgeInsets = NSEdgeInsets(top: 7, left: 9, bottom: 7, right: 9)
        previewCopyText = ActionButton("Copy text", accessibilityLabel: "Copy recognized image text") { [weak self] in self?.copyAs(.text) }
        previewCopyBase64 = ActionButton("Copy Base64", accessibilityLabel: "Copy image as Base64 text") { [weak self] in self?.copyAs(.base64) }
        previewCopyText.identifier = NSUserInterfaceItemIdentifier("history.copy-ocr")
        previewCopyBase64.identifier = NSUserInterfaceItemIdentifier("history.copy-base64")
        let previewHeading = horizontalStack([previewTitle, flexibleSpace(), previewCopyText, previewCopyBase64], spacing: 4)
        preview.addArrangedSubview(previewHeading)
        previewText.isEditable = false
        previewText.isSelectable = true
        previewText.font = .monospacedSystemFont(ofSize: 11, weight: .regular)
        previewText.drawsBackground = false
        previewText.textContainerInset = NSSize(width: 3, height: 3)
        previewText.isVerticallyResizable = true
        previewText.isHorizontallyResizable = false
        previewText.autoresizingMask = [.width]
        previewText.textContainer?.widthTracksTextView = true
        previewText.setAccessibilityLabel("Full selected clip preview")
        previewScroll.documentView = previewText
        previewScroll.hasVerticalScroller = true
        previewScroll.drawsBackground = false
        previewScroll.translatesAutoresizingMaskIntoConstraints = false
        previewImage.imageScaling = .scaleProportionallyUpOrDown
        previewImage.setAccessibilityLabel("Selected clipboard image preview")
        previewImage.translatesAutoresizingMaskIntoConstraints = false
        preview.addArrangedSubview(previewScroll)
        preview.addArrangedSubview(previewImage)
        let preferredPreviewHeight = preview.heightAnchor.constraint(equalToConstant: 128)
        preferredPreviewHeight.priority = .defaultHigh
        preferredPreviewHeight.isActive = true
        preview.heightAnchor.constraint(greaterThanOrEqualToConstant: 60).isActive = true
        previewHeading.widthAnchor.constraint(equalTo: preview.widthAnchor, constant: -18).isActive = true
        previewHeading.heightAnchor.constraint(equalToConstant: 22).isActive = true
        previewScroll.widthAnchor.constraint(equalTo: preview.widthAnchor, constant: -18).isActive = true
        previewImage.widthAnchor.constraint(equalTo: preview.widthAnchor, constant: -18).isActive = true
        previewScroll.heightAnchor.constraint(equalTo: preview.heightAnchor, constant: -40).isActive = true
        previewImage.heightAnchor.constraint(equalTo: preview.heightAnchor, constant: -40).isActive = true
        preview.isHidden = true

        pauseButton = ActionButton("Pause", accessibilityLabel: "Pause clipboard capture") { [weak self] in self?.model.togglePause() }
        let settingsButton = ActionButton("Settings") { [weak self] in
            guard let self else { return }
            self.presentingDialog = true
            self.model.showSettings()
            self.presentingDialog = false
        }
        let clearButton = ActionButton("Clear", accessibilityLabel: "Clear clipboard history") { [weak self] in self?.confirmClear() }
        pauseButton.identifier = NSUserInterfaceItemIdentifier("history.pause")
        settingsButton.identifier = NSUserInterfaceItemIdentifier("history.settings")
        clearButton.identifier = NSUserInterfaceItemIdentifier("history.clear")
        let footer = horizontalStack([countLabel, flexibleSpace(), pauseButton, settingsButton, clearButton], spacing: 4)

        let body = NSStackView(views: [warningLabel, listScroll, actions, preview, footer])
        body.orientation = .vertical
        body.distribution = .fill
        body.alignment = .leading
        body.spacing = 5
        body.edgeInsets = NSEdgeInsets(top: 6, left: 12, bottom: 7, right: 12)
        body.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(header)
        root.addSubview(body)
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: root.topAnchor),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            body.topAnchor.constraint(equalTo: header.bottomAnchor),
            body.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            body.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            body.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            listScroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 62)
        ])
        for view in [warningLabel, listScroll, actions, preview, footer] {
            view.widthAnchor.constraint(equalTo: body.widthAnchor, constant: -24).isActive = true
        }
        actions.heightAnchor.constraint(equalToConstant: 26).isActive = true
        footer.heightAnchor.constraint(equalToConstant: 26).isActive = true
    }

    func showNearPointer() {
        search.stringValue = ""
        scope.selectedSegment = 0
        kinds.selectedSegment = 0
        selectedID = nil
        copyTarget = .image
        previewExpanded = false
        refresh()
        guard let window else { return }
        let pointer = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(pointer, $0.frame, false) } ?? NSScreen.main
        if let bounds = screen?.visibleFrame {
            var frame = window.frame
            let titleBarHeight = frame.height - (window.contentView?.frame.height ?? frame.height)
            window.contentMinSize = NSSize(width: min(420, bounds.width),
                                          height: min(460, max(1, bounds.height - titleBarHeight)))
            frame.size.width = min(frame.width, bounds.width)
            frame.size.height = min(frame.height, bounds.height)
            frame.origin.x = min(max(pointer.x - frame.width / 2, bounds.minX), bounds.maxX - frame.width)
            frame.origin.y = min(max(pointer.y - frame.height + 24, bounds.minY), bounds.maxY - frame.height)
            window.setFrame(frame, display: true)
        }
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(search)
    }

    func hide() { window?.orderOut(nil) }

    func refresh() {
        model.pruneHistory()
        let kindOptions: [ClipKind?] = [nil, .text, .link, .code, .image]
        let kind = kindOptions[max(0, min(kinds.selectedSegment, kindOptions.count - 1))]
        entries = model.history.filtered(query: search.stringValue, pinnedOnly: scope.selectedSegment == 1, kind: kind)
        if !entries.contains(where: { $0.id == selectedID }) {
            selectedID = entries.first?.id
            copyTarget = entries.first?.kind == .image ? .image : .text
        }
        if let entry = selectedEntry { normalizeTarget(for: entry) }
        updatingSelection = true
        table.reloadData()
        if let index = entries.firstIndex(where: { $0.id == selectedID }) {
            table.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        } else {
            table.deselectAll(nil)
        }
        updatingSelection = false
        var messages: [String] = []
        if model.isDemo { messages.append("Demo · Synthetic clips; copying is simulated.") }
        if model.isPaused { messages.append("Clipboard capture is paused.") }
        if let warning = model.warning, !warning.isEmpty { messages.append(warning) }
        if entries.isEmpty { messages.append(search.stringValue.isEmpty ? "No clips here yet. Copy something to get started." : "No matching clips.") }
        warningLabel.stringValue = messages.joined(separator: " ")
        warningLabel.toolTip = warningLabel.stringValue
        warningLabel.isHidden = messages.isEmpty
        countLabel.stringValue = "\(entries.count) \(entries.count == 1 ? "clip" : "clips")"
        pauseButton.title = model.isPaused ? "Resume" : "Pause"
        pauseButton.setAccessibilityLabel(model.isPaused ? "Resume clipboard capture" : "Pause clipboard capture")
        updateActionsAndPreview()
    }

    private var selectedEntry: ClipEntry? { entries.first { $0.id == selectedID } }

    private func normalizeTarget(for entry: ClipEntry) {
        if entry.kind != .image { copyTarget = .text; return }
        if copyTarget == .text && !hasOCR(entry) { copyTarget = .image }
    }

    private func hasOCR(_ entry: ClipEntry) -> Bool {
        entry.ocrStatus == .complete && !entry.ocrText.isEmpty
    }

    @objc private func filtersChanged() { refresh() }
    func controlTextDidChange(_ notification: Notification) { refresh() }

    func numberOfRows(in tableView: NSTableView) -> Int { entries.count }

    func tableView(_ tableView: NSTableView, heightOfRow row: Int) -> CGFloat {
        entries[row].kind == .image ? 100 : 68
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let entry = entries[row]
        let cell = NSTableCellView()
        let kindName = kindTitle(entry.kind)
        let title = label(entry.kind == .image ? "Image" : entry.text, size: 12, weight: .medium)
        title.maximumNumberOfLines = entry.kind == .image ? 1 : 2
        title.lineBreakMode = .byTruncatingTail
        title.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        let metadata = label("\(entry.isPinned ? "Pinned · " : "")\(kindName) · \(dateText(entry.capturedAt))", size: 10, color: .secondaryLabelColor)
        let textStack = NSStackView(views: [title, metadata])
        textStack.orientation = .vertical
        textStack.alignment = .leading
        textStack.spacing = 4
        textStack.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(textStack)
        title.widthAnchor.constraint(equalTo: textStack.widthAnchor).isActive = true
        metadata.widthAnchor.constraint(equalTo: textStack.widthAnchor).isActive = true
        metadata.lineBreakMode = .byTruncatingTail
        var textLeading = cell.leadingAnchor
        var leadingInset: CGFloat = 9
        if entry.kind == .image {
            let thumbnail = NSImageView()
            thumbnail.image = Data(base64Encoded: entry.text).flatMap(NSImage.init(data:))
            thumbnail.imageScaling = .scaleProportionallyUpOrDown
            thumbnail.translatesAutoresizingMaskIntoConstraints = false
            thumbnail.setAccessibilityLabel("Clipboard image thumbnail")
            cell.addSubview(thumbnail)
            NSLayoutConstraint.activate([
                thumbnail.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 9),
                thumbnail.topAnchor.constraint(equalTo: cell.topAnchor, constant: 8),
                thumbnail.widthAnchor.constraint(equalToConstant: 48),
                thumbnail.heightAnchor.constraint(equalToConstant: 48)
            ])
            textLeading = thumbnail.trailingAnchor
            leadingInset = 8
            let ocr = label(ocrDescription(entry), size: 10, color: .secondaryLabelColor)
            textStack.addArrangedSubview(ocr)
            let targets = availableTargets(for: entry)
            let buttons = targets.map { target -> ActionButton in
                let button = ActionButton(targetTitle(target), accessibilityLabel: "Select \(targetTitle(target)) format for clipboard image") { [weak self] in
                    self?.select(entry.id, target: target)
                }
                button.bezelStyle = .roundRect
                button.setButtonType(.pushOnPushOff)
                button.state = entry.id == selectedID && target == copyTarget ? .on : .off
                return button
            }
            let formats = horizontalStack(buttons)
            cell.addSubview(formats)
            NSLayoutConstraint.activate([
                formats.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 9),
                formats.bottomAnchor.constraint(equalTo: cell.bottomAnchor, constant: -6),
                formats.heightAnchor.constraint(equalToConstant: 24),
                formats.trailingAnchor.constraint(lessThanOrEqualTo: cell.trailingAnchor, constant: -9)
            ])
        }
        NSLayoutConstraint.activate([
            textStack.leadingAnchor.constraint(equalTo: textLeading, constant: leadingInset),
            textStack.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -9),
            textStack.topAnchor.constraint(equalTo: cell.topAnchor, constant: 8)
        ])
        cell.setAccessibilityLabel("\(entry.isPinned ? "Pinned. " : "")\(kindName). \(entry.kind == .image ? ocrDescription(entry) : entry.text)")
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !updatingSelection, table.selectedRow >= 0, table.selectedRow < entries.count else { return }
        let entry = entries[table.selectedRow]
        let changed = selectedID != entry.id
        selectedID = entry.id
        if changed { copyTarget = entry.kind == .image ? .image : .text }
        normalizeTarget(for: entry)
        // Refresh format controls without changing row order or the current selection.
        table.reloadData(forRowIndexes: IndexSet(integersIn: 0..<entries.count), columnIndexes: IndexSet(integer: 0))
        updateActionsAndPreview()
    }

    private func select(_ id: UUID, target: CopyTarget? = nil) {
        selectedID = id
        if let target { copyTarget = target }
        refresh()
        if table.selectedRow >= 0 { table.scrollRowToVisible(table.selectedRow) }
    }

    private func updateActionsAndPreview() {
        let entry = selectedEntry
        copyButton.isEnabled = entry != nil
        pinButton.isEnabled = entry != nil
        deleteButton.isEnabled = entry != nil
        previewButton.isEnabled = entry != nil
        pinButton.title = entry?.isPinned == true ? "Unpin" : "Pin"
        copyButton.title = entry?.kind == .image ? "Copy \(targetTitle(copyTarget))" : "Copy"
        previewButton.title = previewExpanded ? "Hide preview" : "Preview"
        preview.isHidden = !previewExpanded || entry == nil
        guard let entry, previewExpanded else { return }
        previewCopyText.isHidden = entry.kind != .image
        previewCopyText.isEnabled = hasOCR(entry)
        previewCopyBase64.isHidden = entry.kind != .image
        let showImage = entry.kind == .image && copyTarget == .image
        previewImage.isHidden = !showImage
        previewScroll.isHidden = showImage
        previewTitle.stringValue = entry.kind == .image ? "\(targetTitle(copyTarget)) preview" : "Full text"
        if showImage {
            previewImage.image = Data(base64Encoded: entry.text).flatMap(NSImage.init(data:))
        } else {
            previewText.string = entry.kind == .image && copyTarget == .text ? entry.ocrText : entry.text
            previewText.scrollToBeginningOfDocument(nil)
        }
    }

    @objc private func copySelection() {
        guard let entry = selectedEntry else { return }
        if model.copy(entry, target: copyTarget), !model.isDemo { hide() }
    }

    private func copyAs(_ target: CopyTarget) {
        guard let entry = selectedEntry else { return }
        copyTarget = target
        updateActionsAndPreview()
        if model.copy(entry, target: target), !model.isDemo { hide() }
    }

    private func pinSelection() { if let id = selectedID { model.togglePin(id) } }
    private func deleteSelection() { if let id = selectedID { model.remove(id) } }
    private func togglePreview() { previewExpanded.toggle(); updateActionsAndPreview() }

    private func confirmClear() {
        presentingDialog = true
        defer { presentingDialog = false }
        let alert = NSAlert()
        alert.messageText = "Clear clipboard history?"
        alert.informativeText = "This removes every clip, including pinned clips. Your current system clipboard stays available."
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Clear History")
        if alert.runModal() == .alertSecondButtonReturn { model.clear() }
    }

    func windowDidResignKey(_ notification: Notification) {
        // Settings is a separate owned controller; defer until AppKit has made it key.
        DispatchQueue.main.async { [weak self] in
            guard let self, !self.model.isDemo, !self.presentingDialog,
                  self.window?.attachedSheet == nil, NSApp.modalWindow == nil else { return }
            if NSApp.keyWindow is SettingsWindow { return }
            self.hide()
        }
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool { hide(); return false }

    private var searchEditor: NSTextView? {
        guard let editor = search.currentEditor() as? NSTextView,
              window?.firstResponder === editor else { return nil }
        return editor
    }

    private func handleKey(_ event: NSEvent) -> Bool {
        let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let command = modifiers.contains(.command)
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if command && !modifiers.contains(.control) && !modifiers.contains(.option) {
            if key == "f" { window?.makeFirstResponder(search); search.selectText(nil); return true }
            if key == "c" {
                if let editor = searchEditor, editor.selectedRange().length > 0 { return false }
                if window?.firstResponder === previewText, previewText.selectedRange().length > 0 { return false }
                copySelection(); return true
            }
            if key == "p" { pinSelection(); return true }
            if event.keyCode == 49 { togglePreview(); return true }
        }
        guard modifiers.intersection([.command, .control, .option]).isEmpty else { return false }
        // The read-only full preview still supports ordinary text selection/navigation.
        if window?.firstResponder === previewText, event.keyCode != 53 { return false }
        switch event.keyCode {
        case 53: hide(); return true
        case 36, 76: copySelection(); return true
        case 125: moveSelection(1); return true
        case 126: moveSelection(-1); return true
        case 123: return cycleTarget(-1, editor: searchEditor)
        case 124: return cycleTarget(1, editor: searchEditor)
        case 51, 117:
            guard window?.firstResponder === table else { return false }
            deleteSelection(); return true
        default: return false
        }
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        switch NSStringFromSelector(commandSelector) {
        case "moveUp:": moveSelection(-1); return true
        case "moveDown:": moveSelection(1); return true
        case "moveLeft:": return cycleTarget(-1, editor: textView)
        case "moveRight:": return cycleTarget(1, editor: textView)
        case "insertNewline:": copySelection(); return true
        case "cancelOperation:": hide(); return true
        default: return false
        }
    }

    private func moveSelection(_ delta: Int) {
        guard !entries.isEmpty else { return }
        let current = entries.firstIndex { $0.id == selectedID } ?? 0
        let next = min(max(current + delta, 0), entries.count - 1)
        let entry = entries[next]
        if entry.id != selectedID { copyTarget = entry.kind == .image ? .image : .text }
        select(entry.id)
    }

    private func cycleTarget(_ delta: Int, editor: NSTextView?) -> Bool {
        guard let entry = selectedEntry, entry.kind == .image else { return false }
        if let editor {
            let range = editor.selectedRange()
            let atEdge = delta < 0 ? range.location == 0 : NSMaxRange(range) == (editor.string as NSString).length
            guard range.length == 0, atEdge else { return false }
        }
        let targets = availableTargets(for: entry)
        let current = targets.firstIndex(of: copyTarget) ?? 0
        copyTarget = targets[(current + delta + targets.count) % targets.count]
        refresh()
        return true
    }

    private func availableTargets(for entry: ClipEntry) -> [CopyTarget] {
        hasOCR(entry) ? [.image, .text, .base64] : [.image, .base64]
    }

    private func targetTitle(_ target: CopyTarget) -> String {
        switch target { case .image: return "Image"; case .text: return "Text"; case .base64: return "Base64" }
    }

    private func kindTitle(_ kind: ClipKind) -> String {
        switch kind { case .text: return "Text"; case .link: return "Link"; case .code: return "Code"; case .image: return "Image" }
    }

    private func ocrDescription(_ entry: ClipEntry) -> String {
        switch entry.ocrStatus {
        case .pending: return "Reading image text…"
        case .complete: return hasOCR(entry) ? "Image text available" : "No text found"
        case .noText: return "No text found"
        case .unavailable: return "Text recognition unavailable"
        case .failed: return "Text recognition failed"
        }
    }

    private func dateText(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = Calendar.current.isDateInToday(date) ? "HH:mm" : "MMM d, HH:mm"
        return formatter.string(from: date)
    }
}
