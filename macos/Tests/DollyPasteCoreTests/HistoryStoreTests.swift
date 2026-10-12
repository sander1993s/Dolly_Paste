import Foundation
import XCTest
@testable import DollyPasteCore

final class HistoryStoreTests: XCTestCase {
    private final class Clock {
        var now = Date(timeIntervalSince1970: 1_800_000_000)
        func advance(_ seconds: TimeInterval) { now.addTimeInterval(seconds) }
    }

    // An actual 1x1 RGBA PNG, independently generated with Python's zlib.
    private let redPixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="

    func testSettingsDefaultsNormalizationAndPartialDecoding() throws {
        XCTAssertEqual(Settings().maxAgeMinutes, 1440)
        XCTAssertEqual(Settings().maxItems, 200)
        XCTAssertFalse(Settings().persistHistory)
        XCTAssertEqual(Settings(maxAgeMinutes: -1, maxItems: 1001), Settings(maxAgeMinutes: 1, maxItems: 1000))
        XCTAssertEqual(Settings(maxAgeMinutes: 50000, maxItems: 0), Settings(maxAgeMinutes: 43200, maxItems: 10))
        let decoder = JSONDecoder()
        XCTAssertEqual(try decoder.decode(Settings.self, from: Data("{}".utf8)), Settings())
        XCTAssertEqual(try decoder.decode(Settings.self, from: Data("{\"maxAgeMinutes\":60}".utf8)),
                       Settings(maxAgeMinutes: 60))
        XCTAssertEqual(try decoder.decode(Settings.self, from: Data("{\"maxItems\":-50}".utf8)).maxItems, 10)
        XCTAssertThrowsError(try decoder.decode(Settings.self, from: Data("{\"maxItems\":\"many\"}".utf8)))
    }

    func testClassificationAndNewestFirstOrder() {
        let store = HistoryStore()
        XCTAssertEqual(store.captureText("Simple text note")?.kind, .text)
        XCTAssertEqual(store.captureText(" HTTPS://example.com/path?q=1 ")?.kind, .link)
        XCTAssertEqual(store.captureText("ftp://example.com/file")?.kind, .link)
        XCTAssertEqual(store.captureText("public class Test { int x = 0; }")?.kind, .code)
        XCTAssertEqual(store.captureText("<p>Hello</p>")?.kind, .code)
        XCTAssertEqual(store.captureText("let x = 1;")?.kind, .code)
        XCTAssertEqual(store.captureText("https://example.com\nsecond line")?.kind, .text)
        XCTAssertEqual(store.captureText("mailto:hello@example.com")?.kind, .text)
        XCTAssertEqual(store.entries.first?.text, "mailto:hello@example.com")
        XCTAssertEqual(store.entries.last?.text, "Simple text note")
    }

    func testTextLimitCountsUTF16RatherThanGraphemesOrBytes() {
        let store = HistoryStore()
        XCTAssertNil(store.captureText(""))
        XCTAssertNil(store.captureText(String(repeating: "a", count: 32769)))
        XCTAssertNotNil(store.captureText(String(repeating: "a", count: 32768)))
        XCTAssertNotNil(store.captureText(String(repeating: "😀", count: 16384)))
        XCTAssertNil(store.captureText(String(repeating: "😀", count: 16384) + "a"))
        XCTAssertNotNil(store.captureText(" \n\t ")) // Windows also retains whitespace-only copies.
    }

    func testDedupPromotesPreservesIdentityCaptureAgePinAndOCR() throws {
        let clock = Clock()
        let store = HistoryStore(now: { clock.now })
        let original = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.togglePin(id: original.id)
        XCTAssertTrue(store.setOCR(id: original.id, text: "Recognized words", status: .complete))
        clock.advance(20)
        store.captureText("newer")
        let recopied = try XCTUnwrap(store.captureImage(base64: redPixel))
        XCTAssertEqual(store.entries.count, 2)
        XCTAssertEqual(store.entries.first?.id, original.id)
        XCTAssertEqual(recopied.id, original.id)
        XCTAssertEqual(recopied.capturedAt, original.capturedAt)
        XCTAssertTrue(recopied.isPinned)
        XCTAssertEqual(recopied.ocrText, "Recognized words")
        XCTAssertEqual(recopied.ocrStatus, .complete)
    }

    func testDedupMatchesExactPayloadBytes() {
        let store = HistoryStore()
        let composed = "caf\u{e9}"
        let decomposed = "cafe\u{301}"
        store.captureText(composed)
        store.captureText(decomposed)
        XCTAssertEqual(store.entries.count, 2, "Different clipboard byte sequences remain separate, as on Windows.")
        store.captureText(composed)
        XCTAssertEqual(store.entries.count, 2)
        XCTAssertTrue(store.entries[0].text.utf8.elementsEqual(composed.utf8))
    }

    func testExactExpiryBoundaryIncludesPinnedItems() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        let entry = try XCTUnwrap(store.captureText("pinned"))
        store.togglePin(id: entry.id)
        clock.advance(59.999)
        XCTAssertFalse(store.prune())
        clock.advance(0.001)
        XCTAssertTrue(store.prune())
        XCTAssertTrue(store.entries.isEmpty)
        XCTAssertFalse(store.prune())
    }

    func testRecopyCannotResurrectExpiredEntry() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        let original = try XCTUnwrap(store.captureText("same"))
        clock.advance(60)
        XCTAssertNil(store.captureText("same"))
        XCTAssertFalse(store.entries.contains(where: { $0.id == original.id }))
    }

    func testCountCapEvictsUnpinnedFirstThenPinned() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxItems: 10), now: { clock.now })
        let oldest = try XCTUnwrap(store.captureText("oldest pinned"))
        store.togglePin(id: oldest.id)
        for index in 1...10 { store.captureText("item \(index)") }
        XCTAssertEqual(store.entries.count, 10)
        XCTAssertTrue(store.entries.contains(where: { $0.id == oldest.id }))
        XCTAssertFalse(store.entries.contains(where: { $0.text == "item 1" }))

        let pinned = (0..<12).map {
            ClipEntry(text: "pinned \($0)", capturedAt: clock.now, isPinned: true, kind: .text)
        }
        store.restore(pinned)
        XCTAssertEqual(store.entries.map(\.id), Array(pinned.prefix(10)).map(\.id))
    }

    func testCountEvictionUsesRecopyOrderWithoutRefreshingAge() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxItems: 10), now: { clock.now })
        let first = try XCTUnwrap(store.captureText("item 0"))
        for index in 1..<10 {
            clock.advance(1)
            store.captureText("item \(index)")
        }
        store.captureText("item 0")
        store.captureText("item 10")
        XCTAssertTrue(store.entries.contains(where: { $0.id == first.id }))
        XCTAssertFalse(store.entries.contains(where: { $0.text == "item 1" }))
        XCTAssertEqual(store.entries.first(where: { $0.id == first.id })?.capturedAt, first.capturedAt)
    }

    func testUpdatingSettingsImmediatelyPrunes() {
        let clock = Clock()
        let store = HistoryStore(now: { clock.now })
        for index in 0..<20 { store.captureText("item \(index)") }
        store.updateSettings(Settings(maxItems: 10))
        XCTAssertEqual(store.entries.count, 10)
        clock.advance(61)
        store.updateSettings(Settings(maxAgeMinutes: 1))
        XCTAssertTrue(store.entries.isEmpty)
    }

    func testOCRNeverRestoresRemovedClearedExpiredOrNonImageItems() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        let removed = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.remove(id: removed.id)
        XCTAssertFalse(store.setOCR(id: removed.id, text: "late", status: .complete))
        let cleared = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.clear()
        XCTAssertFalse(store.setOCR(id: cleared.id, text: "late", status: .complete))
        let expired = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.togglePin(id: expired.id)
        clock.advance(60)
        XCTAssertFalse(store.setOCR(id: expired.id, text: "late", status: .complete))
        XCTAssertTrue(store.entries.isEmpty)
        let text = try XCTUnwrap(store.captureText("plain"))
        XCTAssertFalse(store.setOCR(id: text.id, text: "hidden text", status: .complete))
        XCTAssertEqual(store.entries.first?.ocrText, "")
    }

    func testOCRDoesNotRefreshAgeAndNormalizesResultStates() throws {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        let entry = try XCTUnwrap(store.captureImage(base64: redPixel))
        XCTAssertEqual(entry.ocrStatus, .pending)
        clock.advance(30)
        XCTAssertTrue(store.setOCR(id: entry.id, text: "  first\nsecond \n", status: .complete))
        XCTAssertEqual(store.entries[0].ocrText, "first\nsecond")
        XCTAssertEqual(store.entries[0].capturedAt, entry.capturedAt)
        store.setOCR(id: entry.id, text: " \n\t ", status: .complete)
        XCTAssertEqual(store.entries[0].ocrStatus, .noText)
        for status in [OCRStatus.failed, .unavailable, .pending, .noText] {
            store.setOCR(id: entry.id, text: "must be discarded", status: status)
            XCTAssertEqual(store.entries[0].ocrText, "")
            XCTAssertEqual(store.entries[0].ocrStatus, status)
        }
        clock.advance(30)
        XCTAssertTrue(store.prune())
        XCTAssertTrue(store.entries.isEmpty)
    }

    func testOldOCRCannotAttachToNewCaptureOfSameImage() throws {
        let store = HistoryStore()
        let old = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.remove(id: old.id)
        let replacement = try XCTUnwrap(store.captureImage(base64: redPixel))
        XCTAssertNotEqual(old.id, replacement.id)
        XCTAssertFalse(store.setOCR(id: old.id, text: "stale OCR", status: .complete))
        XCTAssertEqual(store.entries[0].ocrStatus, .pending)
        XCTAssertEqual(store.entries[0].ocrText, "")
        XCTAssertTrue(store.setOCR(id: replacement.id, text: "current OCR", status: .complete))
        XCTAssertEqual(store.entries[0].ocrText, "current OCR")
    }

    func testOCRUTF16TruncationNeverSplitsSurrogatePair() throws {
        let store = HistoryStore()
        let entry = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.setOCR(id: entry.id, text: String(repeating: "a", count: 32767) + "😀tail", status: .complete)
        XCTAssertEqual(store.entries[0].ocrText, String(repeating: "a", count: 32767))
        store.setOCR(id: entry.id, text: String(repeating: "😀", count: 16385), status: .complete)
        XCTAssertEqual(store.entries[0].ocrText, String(repeating: "😀", count: 16384))
        XCTAssertFalse(store.entries[0].ocrText.contains("\u{FFFD}"))
    }

    func testAggregatePayloadCapIncludesImagesOCRAndPins() throws {
        let clock = Clock()
        let store = HistoryStore(now: { clock.now })
        let first = try XCTUnwrap(store.captureImage(base64: paddedPNG(byteCount: 1_800_000, marker: 1)))
        store.togglePin(id: first.id)
        XCTAssertNil(store.captureImage(base64: paddedPNG(byteCount: 1_800_000, marker: 2)),
                     "The only unpinned image is evicted before the pinned image.")
        XCTAssertEqual(store.entries.map(\.id), [first.id])
        let pinned = [
            ClipEntry(text: paddedPNG(byteCount: 1_800_000, marker: 3), capturedAt: clock.now, isPinned: true, kind: .image),
            ClipEntry(text: paddedPNG(byteCount: 1_800_000, marker: 4), capturedAt: clock.now, isPinned: true, kind: .image)
        ]
        store.restore(pinned)
        XCTAssertEqual(store.entries.map(\.id), [pinned[0].id])
        XCTAssertLessThanOrEqual(payloadBytes(store.entries), HistoryStore.maxPayloadBytes)
    }

    func testOCRUTF8BudgetPreservesImageAndScalarBoundaries() throws {
        let store = HistoryStore()
        let entry = try XCTUnwrap(store.captureImage(base64: paddedPNG(byteCount: HistoryStore.maxPNGBytes - 3)))
        XCTAssertEqual(HistoryStore.maxPayloadBytes - entry.text.utf8.count, 4)
        XCTAssertTrue(store.setOCR(id: entry.id, text: "😀tail", status: .complete))
        XCTAssertEqual(store.entries[0].ocrText, "😀")
        XCTAssertEqual(payloadBytes(store.entries), HistoryStore.maxPayloadBytes)
        store.setOCR(id: entry.id, text: "€€tail", status: .complete)
        XCTAssertEqual(store.entries[0].ocrText, "€")
        XCTAssertLessThanOrEqual(payloadBytes(store.entries), HistoryStore.maxPayloadBytes)
    }

    func testOCRWithNoRemainingBytesRetainsImageWithFailedStatus() throws {
        let store = HistoryStore()
        let entry = try XCTUnwrap(store.captureImage(base64: paddedPNG(byteCount: HistoryStore.maxPNGBytes)))
        XCTAssertTrue(store.setOCR(id: entry.id, text: "text", status: .complete))
        XCTAssertEqual(store.entries[0].ocrText, "")
        XCTAssertEqual(store.entries[0].ocrStatus, .failed)
        XCTAssertEqual(store.entries[0].id, entry.id)
    }

    func testOCRPayloadCanEvictAnotherItem() throws {
        let store = HistoryStore()
        let text = try XCTUnwrap(store.captureText(String(repeating: "a", count: 1000)))
        let image = try XCTUnwrap(store.captureImage(base64: paddedPNG(byteCount: HistoryStore.maxPNGBytes - 1500)))
        XCTAssertEqual(store.entries.count, 2)
        XCTAssertTrue(store.setOCR(id: image.id, text: String(repeating: "b", count: 2000), status: .complete))
        XCTAssertFalse(store.entries.contains(where: { $0.id == text.id }))
        XCTAssertEqual(store.entries.map(\.id), [image.id])
        XCTAssertEqual(payloadBytes(store.entries), HistoryStore.maxPayloadBytes)
    }

    func testRestoreRejectsInvalidRecordsAndDeduplicates() {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        let valid = ClipEntry(text: "valid", capturedAt: clock.now, kind: .text)
        let sameID = ClipEntry(id: valid.id, text: "other", capturedAt: clock.now, kind: .text)
        let samePayload = ClipEntry(text: "valid", capturedAt: clock.now, kind: .text)
        let future = ClipEntry(text: "future", capturedAt: clock.now.addingTimeInterval(1), kind: .text)
        let expired = ClipEntry(text: "expired", capturedAt: clock.now.addingTimeInterval(-60), isPinned: true, kind: .text)
        let nan = ClipEntry(text: "nan", capturedAt: Date(timeIntervalSinceReferenceDate: .nan), kind: .text)
        let emptyID = ClipEntry(id: UUID(uuidString: "00000000-0000-0000-0000-000000000000")!, text: "empty id", capturedAt: clock.now, kind: .text)
        let empty = ClipEntry(text: "", capturedAt: clock.now, kind: .text)
        let huge = ClipEntry(text: String(repeating: "a", count: 32769), capturedAt: clock.now, kind: .text)
        let image = ClipEntry(text: "not png", capturedAt: clock.now, kind: .image)
        store.restore([valid, sameID, samePayload, future, expired, nan, emptyID, empty, huge, image])
        XCTAssertEqual(store.entries, [valid])
    }

    func testRestoreSanitizesOCRPreservesOrderAndSupportsMissingOCRKeys() throws {
        let clock = Clock()
        let store = HistoryStore(now: { clock.now })
        let image = ClipEntry(text: redPixel, capturedAt: clock.now.addingTimeInterval(-10), kind: .image,
                              ocrText: "  restored  ", ocrStatus: .complete)
        let text = ClipEntry(text: "note", capturedAt: clock.now, kind: .text, ocrText: "invalid", ocrStatus: .complete)
        store.restore([image, text])
        XCTAssertEqual(store.entries.map(\.id), [image.id, text.id])
        XCTAssertEqual(store.entries[0].ocrText, "restored")
        XCTAssertEqual(store.entries[1].ocrText, "")
        XCTAssertEqual(store.entries[1].ocrStatus, .noText)
        let data = try JSONEncoder().encode(image)
        var object = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        object.removeValue(forKey: "ocrText")
        object.removeValue(forKey: "ocrStatus")
        let decoded = try JSONDecoder().decode(ClipEntry.self, from: JSONSerialization.data(withJSONObject: object))
        XCTAssertEqual(decoded.ocrText, "")
        XCTAssertEqual(decoded.ocrStatus, .pending)
    }

    func testFiltersSearchTextAndOCRButNeverImageBase64() throws {
        let store = HistoryStore()
        let plain = try XCTUnwrap(store.captureText("Hello Dolly"))
        store.captureText("https://example.com/dolly")
        let image = try XCTUnwrap(store.captureImage(base64: redPixel))
        store.setOCR(id: image.id, text: "DOLLY in an image", status: .complete)
        store.togglePin(id: image.id)
        XCTAssertEqual(store.filtered(query: "dolly", pinnedOnly: false, kind: nil).count, 3)
        XCTAssertEqual(store.filtered(query: " dolly ", pinnedOnly: true, kind: nil).map(\.id), [image.id])
        XCTAssertEqual(store.filtered(query: "dolly", pinnedOnly: false, kind: .text).map(\.id), [plain.id])
        XCTAssertTrue(store.filtered(query: "iVBORw", pinnedOnly: false, kind: .image).isEmpty)
        XCTAssertEqual(store.filtered(query: "", pinnedOnly: false, kind: .link).count, 1)
    }

    func testFilterPrunesExpiredHistoryBeforeReturning() {
        let clock = Clock()
        let store = HistoryStore(settings: Settings(maxAgeMinutes: 1), now: { clock.now })
        store.captureText("old")
        clock.advance(60)
        XCTAssertTrue(store.filtered(query: "", pinnedOnly: false, kind: nil).isEmpty)
        XCTAssertTrue(store.entries.isEmpty)
    }

    func testPNGBoundsCorruptionAndTruncationAreRejected() {
        let store = HistoryStore()
        XCTAssertNotNil(store.captureImage(base64: redPixel))
        XCTAssertNil(store.captureImage(base64: "not base64"))
        XCTAssertNil(store.captureImage(base64: Data("not PNG".utf8).base64EncodedString()))
        XCTAssertNil(store.captureImage(base64: String(repeating: "A", count: HistoryStore.maxPayloadBytes + 4)))
        let validBytes = [UInt8](Data(base64Encoded: redPixel)!)
        XCTAssertNil(store.captureImage(base64: Data(validBytes.dropLast()).base64EncodedString()))
        var corrupted = validBytes
        corrupted[45] ^= 1
        XCTAssertNil(store.captureImage(base64: Data(corrupted).base64EncodedString()))
        XCTAssertNil(store.captureImage(base64: Data(validBytes + [0]).base64EncodedString()))
        XCTAssertNil(store.captureImage(base64: makePNG(width: 0, height: 1)))
        XCTAssertNil(store.captureImage(base64: makePNG(width: 8193, height: 1)))
        XCTAssertNil(store.captureImage(base64: makePNG(width: 8192, height: 8192)))
        XCTAssertNotNil(store.captureImage(base64: makePNG(width: 4096, height: 4096)),
                        "The core validates structure/bounds; ImageIO validates actual pixel decoding in the app.")
    }

    func testJSONRoundtripRetainsOriginalCaptureTimePinAndOCR() throws {
        let clock = Clock()
        let original = ClipEntry(text: redPixel, capturedAt: clock.now, isPinned: true, kind: .image,
                                 ocrText: "hello", ocrStatus: .complete)
        let data = try JSONEncoder().encode([original])
        let decoded = try JSONDecoder().decode([ClipEntry].self, from: data)
        let store = HistoryStore(now: { clock.now })
        store.restore(decoded)
        XCTAssertEqual(store.entries, [original])
    }

    private func payloadBytes(_ entries: [ClipEntry]) -> Int {
        entries.reduce(0) { $0 + $1.text.utf8.count + $1.ocrText.utf8.count }
    }

    private func paddedPNG(byteCount: Int, marker: UInt8 = 0) -> String {
        let minimum = 82 // signature + IHDR + private ancillary chunk + IDAT + IEND
        precondition(byteCount >= minimum)
        return makePNG(padding: [UInt8](repeating: marker, count: byteCount - minimum))
    }

    private func makePNG(width: UInt32 = 1, height: UInt32 = 1, padding: [UInt8]? = nil) -> String {
        var bytes: [UInt8] = [137, 80, 78, 71, 13, 10, 26, 10]
        bytes += chunk("IHDR", bigEndian(width) + bigEndian(height) + [8, 6, 0, 0, 0])
        if let padding = padding { bytes += chunk("dpAd", padding) }
        bytes += chunk("IDAT", [120, 156, 99, 248, 207, 192, 240, 31, 0, 5, 0, 1, 255])
        bytes += chunk("IEND", [])
        return Data(bytes).base64EncodedString()
    }

    private func chunk(_ type: String, _ payload: [UInt8]) -> [UInt8] {
        let body = Array(type.utf8) + payload
        var crc: UInt32 = 0xFFFFFFFF
        for byte in body {
            crc ^= UInt32(byte)
            for _ in 0..<8 { crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1 }
        }
        return bigEndian(UInt32(payload.count)) + body + bigEndian(crc ^ 0xFFFFFFFF)
    }

    private func bigEndian(_ value: UInt32) -> [UInt8] {
        [UInt8(truncatingIfNeeded: value >> 24), UInt8(truncatingIfNeeded: value >> 16),
         UInt8(truncatingIfNeeded: value >> 8), UInt8(truncatingIfNeeded: value)]
    }
}
