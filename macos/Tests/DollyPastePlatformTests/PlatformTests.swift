import XCTest
import AppKit
import ImageIO
import UniformTypeIdentifiers
import DollyPasteCore
@testable import DollyPaste

final class ImagePayloadTests: XCTestCase {
    func testRejectsInvalidDimensionsBeforeDecode() {
        XCTAssertFalse(ImagePayload.dimensionsAreAllowed(width: 0, height: 1))
        XCTAssertFalse(ImagePayload.dimensionsAreAllowed(width: 8193, height: 1))
        XCTAssertFalse(ImagePayload.dimensionsAreAllowed(width: 8192, height: 8192))
        XCTAssertTrue(ImagePayload.dimensionsAreAllowed(width: 4096, height: 4096))
        XCTAssertNil(ImagePayload.normalizedPNG(from: Data("not an image".utf8)))
    }

    func testPNGAndTIFFNormalizeToDecodablePNG() throws {
        for input in [try Fixture.image(type: .png), try Fixture.image(type: .tiff)] {
            let output = try XCTUnwrap(ImagePayload.normalizedPNG(from: input))
            XCTAssertTrue(output.starts(with: [137, 80, 78, 71, 13, 10, 26, 10]))
            let source = try XCTUnwrap(CGImageSourceCreateWithData(output as CFData, nil))
            let image = try XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil))
            XCTAssertEqual(image.width, 2)
            XCTAssertEqual(image.height, 2)
        }
    }

    func testTruncatedPNGIsNotAcceptedAsAHeaderOnlyImage() throws {
        let png = try Fixture.image(type: .png)
        XCTAssertNil(ImagePayload.normalizedPNG(from: png.prefix(33)))
    }

    func testValidPNGBytesArePreservedExactly() throws {
        let png = try Fixture.image(type: .png)
        XCTAssertEqual(ImagePayload.normalizedPNG(from: png), png)
    }

    func testOversizedOriginalPNGIsRejectedEvenWhenExtraMetadataCouldBeRemoved() throws {
        var png = try Fixture.image(type: .png)
        png.append(Data(count: ImagePayload.maxPNGBytes))
        XCTAssertNil(ImagePayload.normalizedPNG(from: png))
    }

    func testOutputLimitStopsEncodingAnIncompressibleImage() throws {
        // 1100 x 1100 noisy RGB pixels exceed 3 MiB even though the dimensions
        // and decoded pixel count are valid. This exercises the bounded sink.
        let side = 1100
        var pixels = Data(count: side * side * 4)
        var seed: UInt32 = 0xC0FFEE
        for offset in stride(from: 0, to: pixels.count, by: 4) {
            for channel in 0..<3 {
                seed ^= seed << 13
                seed ^= seed >> 17
                seed ^= seed << 5
                pixels[offset + channel] = UInt8(truncatingIfNeeded: seed)
            }
            pixels[offset + 3] = 255
        }
        let source: Data = try pixels.withUnsafeMutableBytes { buffer in
            let context = try XCTUnwrap(CGContext(data: buffer.baseAddress, width: side, height: side,
                bitsPerComponent: 8, bytesPerRow: side * 4, space: CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
            let image = try XCTUnwrap(context.makeImage())
            let encoded = NSMutableData()
            let destination = try XCTUnwrap(CGImageDestinationCreateWithData(encoded,
                UTType.tiff.identifier as CFString, 1, nil))
            CGImageDestinationAddImage(destination, image, nil)
            XCTAssertTrue(CGImageDestinationFinalize(destination))
            return encoded as Data
        }
        XCTAssertGreaterThan(source.count, ImagePayload.maxPNGBytes)
        XCTAssertNil(ImagePayload.normalizedPNG(from: source))
    }
}

final class ClipboardMonitorTests: XCTestCase {
    private func onMain(_ body: () throws -> Void) rethrows {
        if Thread.isMainThread { try body() } else { try DispatchQueue.main.sync(execute: body) }
    }

    func testDoesNotImportStartupClipboardOrItsOwnCopies() throws {
        try onMain {
            let board = NSPasteboard.withUniqueName()
            defer { board.releaseGlobally() }
            board.setString("already copied", forType: .string)
            var captures = [CapturedPayload]()
            let monitor = ClipboardMonitor(pasteboard: board) { captures.append($0) }
            monitor.poll()
            XCTAssertTrue(captures.isEmpty)
            try monitor.copyText("our copy")
            monitor.poll()
            XCTAssertTrue(captures.isEmpty)
            XCTAssertEqual(board.string(forType: .string), "our copy")
            XCTAssertTrue(board.types?.contains(ClipboardMonitor.ownCopyType) == true)
        }
    }

    func testCaptureIsSingleAndPauseDiscardsIntermediateValues() throws {
        try onMain {
            let board = NSPasteboard.withUniqueName()
            defer { board.releaseGlobally() }
            var texts = [String]()
            let monitor = ClipboardMonitor(pasteboard: board) {
                if case .text(let text) = $0 { texts.append(text) }
            }
            board.clearContents()
            board.setString("first", forType: .string)
            monitor.poll()
            monitor.poll()
            monitor.isPaused = true
            board.clearContents()
            board.setString("while paused", forType: .string)
            monitor.poll()
            monitor.isPaused = false
            monitor.poll()
            board.clearContents()
            board.setString("after resuming", forType: .string)
            monitor.poll()
            XCTAssertEqual(texts, ["first", "after resuming"])
        }
    }

    func testProducerPrivacyMarkersAndFileURLsAreIgnored() throws {
        try onMain {
            let board = NSPasteboard.withUniqueName()
            defer { board.releaseGlobally() }
            var count = 0
            let monitor = ClipboardMonitor(pasteboard: board) { _ in count += 1 }
            for type in ClipboardMonitor.excludedTypes.union([.fileURL]) {
                let item = NSPasteboardItem()
                item.setString("should stay private", forType: .string)
                item.setData(Data(), forType: type)
                board.clearContents()
                board.writeObjects([item])
                monitor.poll()
            }
            XCTAssertEqual(count, 0)
        }
    }

    func testImageWinsOverTextAndCopyProvidesPNGAndTIFF() throws {
        try onMain {
            let board = NSPasteboard.withUniqueName()
            defer { board.releaseGlobally() }
            var capture: CapturedPayload?
            let monitor = ClipboardMonitor(pasteboard: board) { capture = $0 }
            let png = try Fixture.image(type: .png)
            let item = NSPasteboardItem()
            item.setString("image description", forType: .string)
            item.setData(png, forType: .png)
            board.clearContents()
            board.writeObjects([item])
            monitor.poll()
            guard case .image? = capture else { return XCTFail("Expected an image capture") }
            try monitor.copyImage(png)
            XCTAssertEqual(board.data(forType: .png), png)
            XCTAssertNotNil(board.data(forType: .tiff))
            XCTAssertNotNil(board.data(forType: ClipboardMonitor.ownCopyType))
        }
    }

    func testOversizedCaptureAndInvalidImageAreRejected() throws {
        try onMain {
            let board = NSPasteboard.withUniqueName()
            defer { board.releaseGlobally() }
            var count = 0
            let monitor = ClipboardMonitor(pasteboard: board) { _ in count += 1 }
            board.clearContents()
            board.setString(String(repeating: "x", count: 32_769), forType: .string)
            monitor.poll()
            XCTAssertEqual(count, 0)
            board.clearContents()
            board.setString("keep me", forType: .string)
            XCTAssertThrowsError(try monitor.copyImage(Data("bad".utf8)))
            XCTAssertEqual(board.string(forType: .string), "keep me")
        }
    }
}

final class PersistenceStoreTests: XCTestCase {
    private var directory: URL!
    private var keyStore: MemoryKeyStore!

    override func setUpWithError() throws {
        directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("DollyPasteTests-\(UUID().uuidString)", isDirectory: true)
        keyStore = MemoryKeyStore()
    }

    override func tearDownWithError() throws {
        if FileManager.default.fileExists(atPath: directory.path) {
            try FileManager.default.removeItem(at: directory)
        }
    }

    private func makeStore() -> PersistenceStore {
        PersistenceStore(directory: directory, keyStore: keyStore)
    }

    private var entry: ClipEntry {
        ClipEntry(text: "private payload \(String(repeating: "x", count: 80))",
                  capturedAt: Date(timeIntervalSince1970: 1_780_000_000), kind: .text)
    }

    func testSettingsAndMissingHistoryNeverAccessKeychain() throws {
        let store = makeStore()
        XCTAssertEqual(store.loadSettings(), Settings())
        XCTAssertFalse(store.settingsReadFailed)
        XCTAssertEqual(try store.loadHistory(), [])
        let settings = Settings(maxAgeMinutes: 600, maxItems: 300, persistHistory: false)
        try store.saveSettings(settings)
        XCTAssertEqual(store.loadSettings(), settings)
        XCTAssertEqual(keyStore.loads, 0)
        XCTAssertEqual(keyStore.saves, 0)
    }

    func testCorruptSettingsAreDistinguishedFromMissingSettingsAndPreserveHistory() throws {
        let store = makeStore()
        try store.saveHistory([entry])
        let historyURL = directory.appendingPathComponent("history.dat")
        let originalHistory = try Data(contentsOf: historyURL)
        try Data("{broken".utf8).write(to: directory.appendingPathComponent("settings.json"))
        XCTAssertEqual(store.loadSettings(), Settings())
        XCTAssertTrue(store.settingsReadFailed)
        XCTAssertEqual(try Data(contentsOf: historyURL), originalHistory)
        try store.saveSettings(Settings())
        XCTAssertFalse(store.settingsReadFailed)
        XCTAssertEqual(store.loadSettings(), Settings())
    }

    func testEncryptedHistoryRoundTripsAndUsesFreshNonces() throws {
        let store = makeStore()
        let entries = [entry]
        try store.saveHistory(entries)
        let first = try Data(contentsOf: directory.appendingPathComponent("history.dat"))
        XCTAssertNil(first.range(of: Data(entries[0].text.utf8)))
        XCTAssertEqual(keyStore.key?.count, 32)
        XCTAssertEqual(try makeStore().loadHistory(), entries)
        try store.saveHistory(entries)
        let second = try Data(contentsOf: directory.appendingPathComponent("history.dat"))
        XCTAssertNotEqual(first, second)
        XCTAssertEqual(keyStore.saves, 1)
    }

    func testCiphertextTamperingPreventsLoadAndSubsequentOverwrite() throws {
        try makeStore().saveHistory([entry])
        let file = directory.appendingPathComponent("history.dat")
        var bytes = try Data(contentsOf: file)
        bytes[bytes.count - 1] ^= 1
        try bytes.write(to: file)
        let store = makeStore()
        XCTAssertThrowsError(try store.loadHistory())
        XCTAssertThrowsError(try store.saveHistory([]))
        XCTAssertEqual(try Data(contentsOf: file), bytes)
        try store.deleteHistory()
        try store.saveHistory([])
        XCTAssertEqual(try makeStore().loadHistory(), [])
    }

    func testExistingHistoryMustBeLoadedBeforeReplacingIt() throws {
        try makeStore().saveHistory([entry])
        let store = makeStore()
        XCTAssertThrowsError(try store.saveHistory([]))
        XCTAssertEqual(try store.loadHistory().count, 1)
        try store.saveHistory([])
    }

    func testMissingKeyNeverReplacesUnreadableHistoryOrCreatesNewKey() throws {
        try makeStore().saveHistory([entry])
        keyStore.key = nil
        keyStore.saves = 0
        let store = makeStore()
        XCTAssertThrowsError(try store.loadHistory())
        XCTAssertThrowsError(try store.saveHistory([]))
        XCTAssertEqual(keyStore.saves, 0)
    }

    func testStoragePermissionsAndBoundedRead() throws {
        let store = makeStore()
        try store.saveHistory([entry])
        let directoryAttributes = try FileManager.default.attributesOfItem(atPath: directory.path)
        let file = directory.appendingPathComponent("history.dat")
        let fileAttributes = try FileManager.default.attributesOfItem(atPath: file.path)
        XCTAssertEqual((directoryAttributes[.posixPermissions] as? NSNumber)?.intValue, 0o700)
        XCTAssertEqual((fileAttributes[.posixPermissions] as? NSNumber)?.intValue, 0o600)
        let handle = try FileHandle(forWritingTo: file)
        try handle.truncate(atOffset: UInt64(PersistenceStore.maxFileBytes + 1))
        try handle.close()
        XCTAssertThrowsError(try makeStore().loadHistory())
    }

    func testImagePayloadGetsRealDecodeBeforePersistence() throws {
        let malformed = ClipEntry(text: Data("not a PNG".utf8).base64EncodedString(),
                                  capturedAt: Date(), kind: .image)
        XCTAssertThrowsError(try makeStore().saveHistory([malformed]))
        XCTAssertEqual(keyStore.saves, 0)
        let image = ClipEntry(text: try Fixture.image(type: .png).base64EncodedString(),
                              capturedAt: Date(), kind: .image)
        try makeStore().saveHistory([image])
        XCTAssertEqual(try makeStore().loadHistory(), [image])
    }

    func testSymlinkStorageDirectoryCannotRedirectReadsWritesOrDeletion() throws {
        let target = directory.appendingPathComponent("target", isDirectory: true)
        try FileManager.default.createDirectory(at: target, withIntermediateDirectories: true)
        let original = Data("untouched target".utf8)
        let file = target.appendingPathComponent("history.dat")
        try original.write(to: file)
        let link = directory.appendingPathComponent("redirect", isDirectory: true)
        try FileManager.default.createSymbolicLink(at: link, withDestinationURL: target)
        let store = PersistenceStore(directory: link, keyStore: keyStore)
        XCTAssertThrowsError(try store.loadHistory())
        XCTAssertThrowsError(try store.saveSettings(Settings()))
        XCTAssertThrowsError(try store.deleteHistory())
        XCTAssertEqual(try Data(contentsOf: file), original)
        XCTAssertEqual(keyStore.loads, 0)
    }
}

private final class MemoryKeyStore: HistoryKeyStore {
    var key: Data?
    var loads = 0
    var saves = 0
    func loadKey() throws -> Data? { loads += 1; return key }
    func saveKey(_ data: Data) throws { saves += 1; key = data }
}

private enum Fixture {
    static func image(type: UTType) throws -> Data {
        let context = try XCTUnwrap(CGContext(data: nil, width: 2, height: 2, bitsPerComponent: 8,
            bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0.8, green: 0.2, blue: 0.3, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 2, height: 2))
        let image = try XCTUnwrap(context.makeImage())
        let data = NSMutableData()
        let destination = try XCTUnwrap(CGImageDestinationCreateWithData(data, type.identifier as CFString, 1, nil))
        let properties: [CFString: Any] = type == .png ? [
            kCGImagePropertyPNGDictionary: [kCGImagePropertyPNGTitle: "Dolly Paste original PNG"]
        ] : [:]
        CGImageDestinationAddImage(destination, image, properties as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
        return data as Data
    }
}
