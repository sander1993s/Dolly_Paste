import Foundation

/// Platform-independent history rules. Call from a single serialized context;
/// the macOS app owns this store on its main thread.
public final class HistoryStore {
    public static let maxTextUTF16Count = 32768
    public static let maxPayloadBytes = 4 * 1024 * 1024
    public static let maxPNGBytes = 3 * 1024 * 1024
    public static let maxImageDimension = 8192
    public static let maxImagePixels = 16 * 1024 * 1024

    public private(set) var entries: [ClipEntry] = []
    public private(set) var settings: Settings
    private let now: () -> Date

    public init(settings: Settings = Settings(), now: @escaping () -> Date = Date.init) {
        var normalized = settings
        normalized.normalize()
        self.settings = normalized
        self.now = now
    }

    @discardableResult
    public func captureText(_ text: String) -> ClipEntry? {
        guard !text.isEmpty, text.utf16.count <= Self.maxTextUTF16Count else { return nil }
        return capture(text, kind: Self.classify(text))
    }

    @discardableResult
    public func captureImage(base64: String) -> ClipEntry? {
        guard PNGPayload.isValid(base64) else { return nil }
        return capture(base64, kind: .image)
    }

    private func capture(_ text: String, kind: ClipKind) -> ClipEntry? {
        let entry: ClipEntry
        if let index = entries.firstIndex(where: { $0.kind == kind && $0.text.utf8.elementsEqual(text.utf8) }) {
            // Recopying promotes an item, but cannot extend its lifetime or erase OCR/pins.
            entry = entries.remove(at: index)
        } else {
            entry = ClipEntry(text: text, capturedAt: now(), kind: kind)
        }
        entries.insert(entry, at: 0)
        prune()
        return entries.first(where: { $0.id == entry.id })
    }

    /// Late OCR work is allowed to update only a still-retained image.
    @discardableResult
    public func setOCR(id: UUID, text: String, status: OCRStatus) -> Bool {
        prune()
        guard let index = entries.firstIndex(where: { $0.id == id && $0.kind == .image }) else {
            return false
        }
        entries[index] = Self.applyingOCR(text: text, status: status, to: entries[index])
        prune()
        return entries.contains(where: { $0.id == id })
    }

    public func togglePin(id: UUID) {
        prune()
        guard let index = entries.firstIndex(where: { $0.id == id }) else { return }
        entries[index].isPinned.toggle()
    }

    public func remove(id: UUID) {
        entries.removeAll(where: { $0.id == id })
    }

    public func clear() {
        entries.removeAll()
    }

    @discardableResult
    public func prune() -> Bool {
        let initialCount = entries.count
        let instant = now()
        let lifetime = TimeInterval(settings.maxAgeMinutes) * 60
        // Pins never bypass expiration, the item count, or the aggregate payload cap.
        entries.removeAll(where: { instant.timeIntervalSince($0.capturedAt) >= lifetime })
        while entries.count > settings.maxItems {
            entries.remove(at: evictionIndex())
        }
        var retainedBytes = entries.reduce(0) { $0 + Self.payloadBytes($1) }
        while retainedBytes > Self.maxPayloadBytes, !entries.isEmpty {
            let removed = entries.remove(at: evictionIndex())
            retainedBytes -= Self.payloadBytes(removed)
        }
        return entries.count != initialCount
    }

    /// Match Windows: remove from the end of display order, unpinned items first.
    private func evictionIndex() -> Int {
        entries.lastIndex(where: { !$0.isPinned }) ?? (entries.count - 1)
    }

    public func updateSettings(_ settings: Settings) {
        var normalized = settings
        normalized.normalize()
        self.settings = normalized
        prune()
    }

    /// Preserve saved recopy order while rejecting corrupt or out-of-bounds records.
    /// The macOS adapter additionally verifies that image bytes decode with ImageIO.
    public func restore(_ savedEntries: [ClipEntry]) {
        let instant = now()
        var seenIDs = Set<UUID>()
        var seenPayloads = Set<Data>()
        let emptyID = UUID(uuidString: "00000000-0000-0000-0000-000000000000")!
        entries = savedEntries.compactMap { entry in
            guard entry.id != emptyID,
                  entry.capturedAt.timeIntervalSinceReferenceDate.isFinite,
                  entry.capturedAt <= instant,
                  instant.timeIntervalSince(entry.capturedAt) < TimeInterval(settings.maxAgeMinutes) * 60,
                  !seenIDs.contains(entry.id) else { return nil }
            if entry.kind == .image {
                guard PNGPayload.isValid(entry.text) else { return nil }
            } else {
                guard !entry.text.isEmpty, entry.text.utf16.count <= Self.maxTextUTF16Count else { return nil }
            }
            let key = Data((entry.kind.rawValue + "\u{0}" + entry.text).utf8)
            guard seenPayloads.insert(key).inserted else { return nil }
            seenIDs.insert(entry.id)
            return Self.applyingOCR(text: entry.ocrText, status: entry.ocrStatus, to: entry)
        }
        prune()
    }

    public func filtered(query: String, pinnedOnly: Bool, kind: ClipKind?) -> [ClipEntry] {
        prune()
        let needle = query.trimmingCharacters(in: .whitespacesAndNewlines)
        return entries.filter { entry in
            guard (!pinnedOnly || entry.isPinned), (kind == nil || entry.kind == kind) else { return false }
            guard !needle.isEmpty else { return true }
            let searchable = entry.kind == .image ? entry.ocrText : entry.text
            return searchable.range(of: needle, options: [.caseInsensitive]) != nil
        }
    }

    private static func applyingOCR(text: String, status: OCRStatus, to original: ClipEntry) -> ClipEntry {
        var entry = original
        guard entry.kind == .image else {
            entry.ocrText = ""
            entry.ocrStatus = .noText
            return entry
        }
        entry.ocrStatus = status
        guard status == .complete else {
            entry.ocrText = ""
            return entry
        }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        let characterBounded = boundedPrefix(trimmed, utf16Limit: maxTextUTF16Count, byteLimit: .max)
        guard !characterBounded.isEmpty else {
            entry.ocrText = ""
            entry.ocrStatus = .noText
            return entry
        }
        // OCR may evict other history, but never its own image solely because the
        // image already occupies almost the whole payload budget.
        entry.ocrText = boundedPrefix(characterBounded, utf16Limit: maxTextUTF16Count,
                                      byteLimit: max(0, maxPayloadBytes - entry.text.utf8.count))
        if entry.ocrText.isEmpty { entry.ocrStatus = .failed }
        return entry
    }

    /// Stop only at Unicode scalar boundaries, never in a surrogate pair or UTF-8 sequence.
    private static func boundedPrefix(_ text: String, utf16Limit: Int, byteLimit: Int) -> String {
        var utf16Count = 0
        var byteCount = 0
        var end = text.unicodeScalars.startIndex
        for scalar in text.unicodeScalars {
            let utf16Length = scalar.value > 0xFFFF ? 2 : 1
            let byteLength: Int
            switch scalar.value {
            case 0...0x7F: byteLength = 1
            case 0...0x7FF: byteLength = 2
            case 0...0xFFFF: byteLength = 3
            default: byteLength = 4
            }
            guard utf16Count + utf16Length <= utf16Limit, byteCount + byteLength <= byteLimit else { break }
            utf16Count += utf16Length
            byteCount += byteLength
            end = text.unicodeScalars.index(after: end)
        }
        return String(text.unicodeScalars[..<end])
    }

    private static func payloadBytes(_ entry: ClipEntry) -> Int {
        entry.text.utf8.count + entry.ocrText.utf8.count
    }

    private static func classify(_ text: String) -> ClipKind {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if !trimmed.contains("\n"), !trimmed.contains("\r"),
           let url = URL(string: trimmed), let scheme = url.scheme?.lowercased(),
           ["http", "https", "ftp"].contains(scheme),
           let host = url.host, !host.isEmpty {
            return .link
        }
        guard trimmed.utf16.count >= 6 else { return .text }
        let braces = trimmed.contains("{") && trimmed.contains("}")
        let semicolons = trimmed.contains(";")
        let html = trimmed.contains("<") && trimmed.contains(">") &&
            (trimmed.contains("</") || trimmed.contains("/>"))
        if html || (braces && (semicolons || trimmed.contains("\n"))) { return .code }
        let keywords = [
            "public ", "private ", "protected ", "internal ", "class ", "namespace ",
            "function ", "def ", "import ", "using ", "return ", "var ", "let ", "const ",
            "if (", "while (", "for (", "foreach (", "switch (", "catch (",
            "select ", "from ", "where ", "insert into ", "update ", "delete from "
        ]
        let lower = trimmed.lowercased()
        if (trimmed.contains("\n") || semicolons || braces || trimmed.contains("(")),
           keywords.contains(where: { lower.contains($0) }) { return .code }
        return .text
    }
}
