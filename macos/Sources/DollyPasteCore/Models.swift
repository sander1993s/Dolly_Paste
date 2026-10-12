import Foundation

public struct Settings: Codable, Equatable {
    public var maxAgeMinutes: Int
    public var maxItems: Int
    public var persistHistory: Bool

    public init(maxAgeMinutes: Int = 1440, maxItems: Int = 200, persistHistory: Bool = false) {
        self.maxAgeMinutes = maxAgeMinutes
        self.maxItems = maxItems
        self.persistHistory = persistHistory
        normalize()
    }

    public mutating func normalize() {
        maxAgeMinutes = min(43200, max(1, maxAgeMinutes))
        maxItems = min(1000, max(10, maxItems))
    }

    private enum CodingKeys: String, CodingKey {
        case maxAgeMinutes, maxItems, persistHistory
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            maxAgeMinutes: try values.decodeIfPresent(Int.self, forKey: .maxAgeMinutes) ?? 1440,
            maxItems: try values.decodeIfPresent(Int.self, forKey: .maxItems) ?? 200,
            persistHistory: try values.decodeIfPresent(Bool.self, forKey: .persistHistory) ?? false
        )
    }
}

public enum ClipKind: String, Codable, CaseIterable {
    case text, link, code, image
}

public enum OCRStatus: String, Codable {
    case pending, complete, noText = "no-text", unavailable, failed
}

public enum CopyTarget: String, CaseIterable {
    case image, text, base64
}

public struct ClipEntry: Identifiable, Codable, Equatable {
    public let id: UUID
    /// Plain text for text entries; PNG bytes encoded as base64 for images.
    public let text: String
    public let capturedAt: Date
    public var isPinned: Bool
    public let kind: ClipKind
    public var ocrText: String
    public var ocrStatus: OCRStatus

    public init(id: UUID = UUID(), text: String, capturedAt: Date, isPinned: Bool = false,
                kind: ClipKind, ocrText: String = "", ocrStatus: OCRStatus? = nil) {
        self.id = id
        self.text = text
        self.capturedAt = capturedAt
        self.isPinned = isPinned
        self.kind = kind
        self.ocrText = ocrText
        self.ocrStatus = ocrStatus ?? (kind == .image ? .pending : .noText)
    }

    private enum CodingKeys: String, CodingKey {
        case id, text, capturedAt, isPinned, kind, ocrText, ocrStatus
    }

    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            id: try values.decode(UUID.self, forKey: .id),
            text: try values.decode(String.self, forKey: .text),
            capturedAt: try values.decode(Date.self, forKey: .capturedAt),
            isPinned: try values.decodeIfPresent(Bool.self, forKey: .isPinned) ?? false,
            kind: try values.decode(ClipKind.self, forKey: .kind),
            ocrText: try values.decodeIfPresent(String.self, forKey: .ocrText) ?? "",
            ocrStatus: try values.decodeIfPresent(OCRStatus.self, forKey: .ocrStatus)
        )
    }
}
