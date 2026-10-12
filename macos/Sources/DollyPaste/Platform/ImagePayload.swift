import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

/// Validates dimensions before decoding, preserves valid PNG bytes, and converts other images to PNG.
enum ImagePayload {
    static let maxPNGBytes = 3 * 1024 * 1024
    static let maxInputBytes = 128 * 1024 * 1024
    static let maxDimension = 8192
    static let maxPixels = 16 * 1024 * 1024

    static func dimensionsAreAllowed(width: Int, height: Int) -> Bool {
        width > 0 && height > 0 && width <= maxDimension && height <= maxDimension &&
            width <= maxPixels / height
    }

    static func normalizedPNG(from data: Data) -> Data? {
        let isPNG = data.starts(with: [137, 80, 78, 71, 13, 10, 26, 10])
        // PNG base64 is a first-class copy action. Preserve the producer's exact
        // bytes, including metadata, but never retain an oversized original PNG.
        guard !isPNG || data.count <= maxPNGBytes else { return nil }
        guard !data.isEmpty, data.count <= maxInputBytes,
              let source = CGImageSourceCreateWithData(data as CFData,
                [kCGImageSourceShouldCache: false] as CFDictionary),
              CGImageSourceGetCount(source) > 0,
              let properties = CGImageSourceCopyPropertiesAtIndex(source, 0,
                [kCGImageSourceShouldCache: false] as CFDictionary) as? [CFString: Any],
              let width = (properties[kCGImagePropertyPixelWidth] as? NSNumber)?.intValue,
              let height = (properties[kCGImagePropertyPixelHeight] as? NSNumber)?.intValue,
              dimensionsAreAllowed(width: width, height: height) else { return nil }

        // CreateThumbnailWithTransform applies TIFF/EXIF orientation. Its limit is
        // the original longest side, so valid inputs retain their full resolution.
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: max(width, height),
            kCGImageSourceShouldCacheImmediately: true
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary),
              CGImageSourceGetStatusAtIndex(source, 0) == .statusComplete,
              dimensionsAreAllowed(width: image.width, height: image.height) else { return nil }
        if isPNG { return data }

        let sink = BoundedPNGData()
        var callbacks = CGDataConsumerCallbacks(putBytes: { context, bytes, count in
            guard let context else { return 0 }
            let sink = Unmanaged<BoundedPNGData>.fromOpaque(context).takeUnretainedValue()
            guard !sink.exceeded, count <= ImagePayload.maxPNGBytes - sink.data.count else {
                sink.exceeded = true
                return 0
            }
            sink.data.append(bytes.assumingMemoryBound(to: UInt8.self), count: count)
            return count
        }, releaseConsumer: nil)
        guard let consumer = CGDataConsumer(info: Unmanaged.passUnretained(sink).toOpaque(),
                                            cbks: &callbacks),
              let destination = CGImageDestinationCreateWithDataConsumer(
                consumer, UTType.png.identifier as CFString, 1, nil) else { return nil }
        return withExtendedLifetime(sink) {
            CGImageDestinationAddImage(destination, image, nil)
            guard CGImageDestinationFinalize(destination), !sink.exceeded,
                  !sink.data.isEmpty else { return nil }
            return sink.data
        }
    }
}

private final class BoundedPNGData {
    var data = Data()
    var exceeded = false
}
