import AppKit
import DollyPasteCore

/// All history and UI mutations occur on the main thread. OCR owns at most one
/// image outside the store; pending work is discovered by ID from live history.
final class AppModel {
    let history: HistoryStore
    let isDemo: Bool
    private(set) var isPaused = false
    var warning: String? { [storageWarning, operationWarning, shortcutWarning].compactMap { $0 }.joined(separator: "\n").nilIfEmpty }
    var onChange: (() -> Void)?
    var onShowSettings: (() -> Void)?
    var shortcutWarning: String? { didSet { onChange?() } }

    private let persistence: PersistenceStore?
    private var clipboard: ClipboardMonitor?
    private let ocr = OCRService()
    private var maintenance: Timer?
    private var activeOCR: UUID?
    private var storageWarning: String?
    private var operationWarning: String?
    private var persistenceBlocked = false
    private var stopped = false

    init(isDemo: Bool) {
        self.isDemo = isDemo
        if isDemo {
            persistence = nil
            history = HistoryStore()
            seedDemo()
        } else {
            let disk = PersistenceStore()
            persistence = disk
            let settings = disk.loadSettings()
            history = HistoryStore(settings: settings)
            do {
                if disk.settingsReadFailed {
                    persistenceBlocked = true
                    storageWarning = "Settings could not be read. This session uses defaults; any saved history has been preserved. Save Settings or clear history to choose how to continue."
                } else if settings.persistHistory {
                    let records = try disk.loadHistory()
                    history.restore(records)
                    // Persist pruning immediately: a restart never extends retention.
                    try disk.saveHistory(history.entries)
                } else {
                    try disk.deleteHistory()
                }
            } catch {
                persistenceBlocked = settings.persistHistory
                storageWarning = "History storage is unavailable. This session stays in memory. \(error.localizedDescription)"
            }
        }
    }

    func start() {
        guard !isDemo, clipboard == nil else { return }
        let monitor = ClipboardMonitor { [weak self] payload in self?.capture(payload) }
        clipboard = monitor
        monitor.start()
        maintenance = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            if self.history.prune() { self.changed() }
        }
        scheduleOCR()
    }

    func stop() {
        stopped = true
        maintenance?.invalidate()
        maintenance = nil
        clipboard?.stop()
        if history.prune() { persist() }
    }

    private func capture(_ payload: CapturedPayload) {
        guard !isPaused, !stopped else { return }
        switch payload {
        case .text(let text): _ = history.captureText(text)
        case .image(let png): _ = history.captureImage(base64: png.base64EncodedString())
        }
        changed()
        scheduleOCR()
    }

    private func scheduleOCR() {
        guard !isDemo, !stopped, activeOCR == nil else { return }
        if history.prune() { changed() }
        guard let entry = history.entries.first(where: { $0.kind == .image && $0.ocrStatus == .pending }) else { return }
        guard let png = Data(base64Encoded: entry.text) else {
            history.setOCR(id: entry.id, text: "", status: .failed)
            changed()
            scheduleOCR()
            return
        }
        activeOCR = entry.id
        ocr.recognize(png) { [weak self] text, status in
            guard let self = self, !self.stopped else { return }
            self.activeOCR = nil
            // The core looks up the ID after pruning; deleted/expired images stay gone.
            self.history.setOCR(id: entry.id, text: text, status: status)
            self.changed()
            self.scheduleOCR()
        }
    }

    @discardableResult
    func copy(_ entry: ClipEntry, target: CopyTarget) -> Bool {
        if history.prune() { changed() }
        guard let live = history.entries.first(where: { $0.id == entry.id }) else {
            operationWarning = "This clip has expired or was removed."
            onChange?()
            return false
        }
        if isDemo {
            operationWarning = "Preview mode — copy simulated; your clipboard is unchanged."
            onChange?()
            return true
        }
        guard let clipboard = clipboard else { return false }
        do {
            if live.kind != .image { try clipboard.copyText(live.text) }
            else {
                switch target {
                case .image:
                    guard let data = Data(base64Encoded: live.text) else { return false }
                    try clipboard.copyImage(data)
                case .base64: try clipboard.copyText(live.text)
                case .text:
                    guard live.ocrStatus == .complete, !live.ocrText.isEmpty else { return false }
                    try clipboard.copyText(live.ocrText)
                }
            }
            operationWarning = nil
            return true
        } catch {
            operationWarning = "Could not copy this clip. \(error.localizedDescription)"
            onChange?()
            return false
        }
    }

    func togglePause() {
        guard !isDemo else { return }
        isPaused.toggle()
        clipboard?.isPaused = isPaused
        onChange?()
    }

    func togglePin(_ id: UUID) { history.togglePin(id: id); changed() }
    func remove(_ id: UUID) { history.remove(id: id); changed() }
    func pruneHistory() { if history.prune() { persist() } }
    func clear() {
        history.clear()
        if !isDemo {
            do {
                try persistence?.deleteHistory()
                persistenceBlocked = false
                storageWarning = nil
            } catch {
                storageWarning = "History was cleared from memory, but the saved file could not be removed. \(error.localizedDescription)"
                onChange?()
                return
            }
        }
        changed()
    }
    func showSettings() { onShowSettings?() }
    func quit() { NSApp.terminate(nil) }

    @discardableResult
    func applySettings(_ proposed: Settings) -> Bool {
        var settings = proposed
        settings.normalize()
        if isDemo {
            // Demo settings apply only to its synthetic in-memory store.
            settings.persistHistory = false
            history.updateSettings(settings)
            onChange?()
            return true
        }
        guard let persistence = persistence else { return false }
        do {
            // Delete first when disabling; never report success if old history remains.
            if !settings.persistHistory { try persistence.deleteHistory() }
            try persistence.saveSettings(settings)
            history.updateSettings(settings)
            if !settings.persistHistory { persistenceBlocked = false; storageWarning = nil }
            changed()
            scheduleOCR()
            return storageWarning == nil
        } catch {
            storageWarning = "Could not save settings. \(error.localizedDescription)"
            onChange?()
            return false
        }
    }

    private func changed() { persist(); onChange?() }

    private func persist() {
        guard !isDemo, history.settings.persistHistory, !persistenceBlocked else { return }
        do {
            try persistence?.saveHistory(history.entries)
            storageWarning = nil
        } catch {
            storageWarning = "Could not save encrypted history. \(error.localizedDescription)"
        }
    }

    private func seedDemo() {
        history.captureText("Shopping list:\n• Rolled oats\n• Whole milk\n• Local wildflower honey\n• Green tea")
        history.captureText("https://example.com/notes")
        history.captureText("function formatTimestamp(date) {\n    return new Intl.DateTimeFormat('en-US').format(date);\n}")
        if let note = history.captureText("Remember to send the meeting notes after lunch.") { history.togglePin(id: note.id) }
        let image = NSImage(size: NSSize(width: 320, height: 120))
        image.lockFocus()
        NSColor(calibratedWhite: 0.96, alpha: 1).setFill()
        NSBezierPath(rect: NSRect(x: 0, y: 0, width: 320, height: 120)).fill()
        ("Dolly Paste" as NSString).draw(at: NSPoint(x: 25, y: 46), withAttributes: [
            .font: NSFont.boldSystemFont(ofSize: 36), .foregroundColor: NSColor.systemBlue
        ])
        image.unlockFocus()
        if let tiff = image.tiffRepresentation, let png = ImagePayload.normalizedPNG(from: tiff),
           let entry = history.captureImage(base64: png.base64EncodedString()) {
            history.setOCR(id: entry.id, text: "Dolly Paste", status: .complete)
        }
    }
}

private extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
