import Foundation

/// A bounded structural check, before platform image decoding allocates pixels.
/// ImageIO in the application remains responsible for decoding compressed pixels.
enum PNGPayload {
    static func isValid(_ base64: String) -> Bool {
        guard !base64.isEmpty, base64.utf8.count <= HistoryStore.maxPayloadBytes,
              let data = Data(base64Encoded: base64), data.count >= 57,
              data.count <= HistoryStore.maxPNGBytes else { return false }
        let bytes = [UInt8](data)
        guard Array(bytes.prefix(8)) == [137, 80, 78, 71, 13, 10, 26, 10],
              read32(bytes, 8) == 13,
              Array(bytes[12..<16]) == [73, 72, 68, 82] else { return false }
        let width = UInt64(read32(bytes, 16))
        let height = UInt64(read32(bytes, 20))
        guard width > 0, height > 0,
              width <= UInt64(HistoryStore.maxImageDimension), height <= UInt64(HistoryStore.maxImageDimension),
              width * height <= UInt64(HistoryStore.maxImagePixels),
              bytes[26] == 0, bytes[27] == 0, bytes[28] <= 1 else { return false }
        let depths: [UInt8: [UInt8]] = [0: [1, 2, 4, 8, 16], 2: [8, 16], 3: [1, 2, 4, 8], 4: [8, 16], 6: [8, 16]]
        guard depths[bytes[25]]?.contains(bytes[24]) == true else { return false }

        var offset = 8
        var foundImageData = false
        while offset <= bytes.count - 12 {
            let length = Int(read32(bytes, offset))
            guard length <= bytes.count - offset - 12 else { return false }
            let type = Array(bytes[(offset + 4)..<(offset + 8)])
            guard type.allSatisfy({ (65...90).contains($0) || (97...122).contains($0) }),
                  crc32(bytes, from: offset + 4, to: offset + 8 + length) == read32(bytes, offset + 8 + length)
            else { return false }
            if type == [73, 72, 68, 82], offset != 8 { return false } // duplicate IHDR
            if type == [73, 68, 65, 84], length > 0 { foundImageData = true } // IDAT
            if type == [73, 69, 78, 68] { // IEND
                return length == 0 && foundImageData && offset + 12 == bytes.count
            }
            offset += length + 12
        }
        return false
    }

    private static func read32(_ bytes: [UInt8], _ offset: Int) -> UInt32 {
        UInt32(bytes[offset]) << 24 | UInt32(bytes[offset + 1]) << 16 |
            UInt32(bytes[offset + 2]) << 8 | UInt32(bytes[offset + 3])
    }

    private static let crcTable: [UInt32] = (0..<256).map { value in
        var crc = UInt32(value)
        for _ in 0..<8 { crc = (crc & 1) == 1 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1 }
        return crc
    }

    private static func crc32(_ bytes: [UInt8], from start: Int, to end: Int) -> UInt32 {
        var crc: UInt32 = 0xFFFFFFFF
        for byte in bytes[start..<end] { crc = crcTable[Int((crc ^ UInt32(byte)) & 0xFF)] ^ (crc >> 8) }
        return crc ^ 0xFFFFFFFF
    }
}
