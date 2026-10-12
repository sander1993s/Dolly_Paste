import Foundation
import Vision
import DollyPasteCore

/// Vision performs recognition locally. The caller schedules one history item at a time.
final class OCRService {
    private let queue = DispatchQueue(label: "be.dollypaste.ocr", qos: .utility)
    private let lock = NSLock()
    private var busy = false

    func recognize(_ png: Data, completion: @escaping (String, OCRStatus) -> Void) {
        lock.lock()
        guard !busy else {
            lock.unlock()
            DispatchQueue.main.async { completion("", .unavailable) }
            return
        }
        busy = true
        lock.unlock()

        queue.async { [self] in
            let result: (String, OCRStatus) = autoreleasepool {
                guard png.count <= ImagePayload.maxPNGBytes,
                      let normalized = ImagePayload.normalizedPNG(from: png) else {
                    return ("", .failed)
                }
                let request = VNRecognizeTextRequest()
                request.recognitionLevel = .accurate
                request.usesLanguageCorrection = true
                request.automaticallyDetectsLanguage = true
                do {
                    try VNImageRequestHandler(data: normalized, options: [:]).perform([request])
                    var text = ""
                    var units = 0
                    for observation in request.results ?? [] {
                        guard let line = observation.topCandidates(1).first?.string else { continue }
                        if !text.isEmpty, units < ClipboardMonitor.maxTextLength {
                            text.append("\n")
                            units += 1
                        }
                        for scalar in line.unicodeScalars {
                            let length = scalar.value > 0xFFFF ? 2 : 1
                            guard units + length <= ClipboardMonitor.maxTextLength else {
                                break
                            }
                            text.unicodeScalars.append(scalar)
                            units += length
                        }
                        if units >= ClipboardMonitor.maxTextLength { break }
                    }
                    text = text.trimmingCharacters(in: .whitespacesAndNewlines)
                    return (text, text.isEmpty ? .noText : .complete)
                } catch {
                    return ("", .failed)
                }
            }
            lock.lock()
            busy = false
            lock.unlock()
            DispatchQueue.main.async { completion(result.0, result.1) }
        }
    }
}
