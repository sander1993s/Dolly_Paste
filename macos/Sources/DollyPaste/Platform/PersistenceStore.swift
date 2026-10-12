import Foundation
import CryptoKit
import Security
import Darwin
import DollyPasteCore

/// Settings contain no clipboard payloads. History is only ever written as authenticated ciphertext.
/// Confine a store instance to the app's main thread (or one serial persistence queue).
final class PersistenceStore {
    static let maxFileBytes = 32 * 1024 * 1024
    private static let magic = Data("DOLLYMAC1".utf8)
    private let directory: URL
    private let keys: HistoryKeyStore
    private var historyWasLoaded = false
    private var historyReadFailed = false
    private(set) var settingsReadFailed = false
    private var validatedImages = Set<Data>()
    private var historyURL: URL { directory.appendingPathComponent("history.dat") }
    private var settingsURL: URL { directory.appendingPathComponent("settings.json") }

    convenience init(directory: URL? = nil) {
        self.init(directory: directory, keyStore: KeychainHistoryKeyStore())
    }

    init(directory: URL?, keyStore: HistoryKeyStore) {
        self.directory = directory ?? FileManager.default.urls(for: .applicationSupportDirectory,
            in: .userDomainMask)[0].appendingPathComponent("Dolly Paste", isDirectory: true)
        keys = keyStore
    }

    func loadSettings() -> Settings {
        do {
            guard let bytes = try read(settingsURL, limit: 64 * 1024) else {
                settingsReadFailed = false
                return Settings()
            }
            let settings = try JSONDecoder().decode(Settings.self, from: bytes)
            settingsReadFailed = false
            return settings
        } catch {
            // Defaults are safe for this session, but are not evidence that the
            // user chose to disable persistence. The app preserves old history.
            settingsReadFailed = true
            return Settings()
        }
    }

    func saveSettings(_ settings: Settings) throws {
        let bytes = try JSONEncoder().encode(settings)
        guard bytes.count <= 64 * 1024 else { throw PersistenceError.tooLarge }
        try atomicWrite(bytes, to: settingsURL)
        settingsReadFailed = false
    }

    func loadHistory() throws -> [ClipEntry] {
        do {
            guard let bytes = try read(historyURL, limit: Self.maxFileBytes) else {
                historyWasLoaded = true
                historyReadFailed = false
                return []
            }
            guard bytes.starts(with: Self.magic),
                  let keyBytes = try keys.loadKey(), keyBytes.count == 32 else {
                throw PersistenceError.cannotDecrypt
            }
            let box = try AES.GCM.SealedBox(combined: bytes.dropFirst(Self.magic.count))
            let plaintext = try AES.GCM.open(box, using: SymmetricKey(data: keyBytes),
                                             authenticating: Self.magic)
            let envelope = try JSONDecoder().decode(HistoryEnvelope.self, from: plaintext)
            guard envelope.version == 1 else { throw PersistenceError.unsupportedVersion }
            try validate(envelope.entries)
            historyWasLoaded = true
            historyReadFailed = false
            return envelope.entries
        } catch {
            // Never replace unreadable history with an empty session on the next save.
            historyReadFailed = true
            throw error
        }
    }

    func saveHistory(_ entries: [ClipEntry]) throws {
        guard !historyReadFailed else { throw PersistenceError.historyNotLoaded }
        if !historyWasLoaded, FileManager.default.fileExists(atPath: historyURL.path) {
            throw PersistenceError.historyNotLoaded
        }
        try validate(entries)
        let plaintext = try JSONEncoder().encode(HistoryEnvelope(version: 1, entries: entries))
        guard plaintext.count <= Self.maxFileBytes - 64 else { throw PersistenceError.tooLarge }

        let keyBytes: Data
        if let existing = try keys.loadKey() {
            guard existing.count == 32 else { throw PersistenceError.cannotDecrypt }
            keyBytes = existing
        } else {
            // A key is created only by the optional history-save path.
            let key = SymmetricKey(size: .bits256)
            keyBytes = key.withUnsafeBytes { Data($0) }
            try keys.saveKey(keyBytes)
        }
        let box = try AES.GCM.seal(plaintext, using: SymmetricKey(data: keyBytes),
                                    authenticating: Self.magic)
        guard let combined = box.combined else { throw PersistenceError.cannotEncrypt }
        var bytes = Self.magic
        bytes.append(combined)
        try atomicWrite(bytes, to: historyURL)
        historyWasLoaded = true
    }

    func deleteHistory() throws {
        _ = try checkDirectory()
        // unlink removes only this exact file, never a symlink's destination.
        let result = historyURL.path.withCString { Darwin.unlink($0) }
        guard result == 0 || errno == ENOENT else { throw posixError() }
        historyWasLoaded = true
        historyReadFailed = false
    }

    private func validate(_ entries: [ClipEntry]) throws {
        guard entries.count <= 1000 else { throw PersistenceError.tooLarge }
        var payloadBytes = 0
        var currentImages = Set<Data>()
        for entry in entries {
            guard entry.ocrText.utf16.count <= ClipboardMonitor.maxTextLength else {
                throw PersistenceError.invalidHistory
            }
            if entry.kind == .image {
                guard entry.text.utf8.count <= 4 * 1024 * 1024,
                      let image = Data(base64Encoded: entry.text),
                      image.count <= ImagePayload.maxPNGBytes,
                      image.starts(with: [137, 80, 78, 71, 13, 10, 26, 10]) else {
                    throw PersistenceError.invalidHistory
                }
                let digest = Data(SHA256.hash(data: image))
                guard validatedImages.contains(digest) || ImagePayload.normalizedPNG(from: image) != nil else {
                    throw PersistenceError.invalidHistory
                }
                currentImages.insert(digest)
            } else {
                guard !entry.text.isEmpty, entry.text.utf16.count <= ClipboardMonitor.maxTextLength else {
                    throw PersistenceError.invalidHistory
                }
            }
            payloadBytes += entry.text.utf8.count + entry.ocrText.utf8.count
            guard payloadBytes <= 4 * 1024 * 1024 else { throw PersistenceError.tooLarge }
        }
        // A digest covers the complete immutable payload, not just an entry ID.
        // Repeated saves do not decode unchanged images; removed images leave the cache.
        validatedImages = currentImages
    }

    private func read(_ url: URL, limit: Int) throws -> Data? {
        // No keychain lookup or directory creation occurs when no file exists.
        _ = try checkDirectory()
        let descriptor = url.path.withCString { Darwin.open($0, O_RDONLY | O_NOFOLLOW | O_NONBLOCK) }
        if descriptor < 0 {
            if errno == ENOENT { return nil }
            throw posixError()
        }
        let handle = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer { try? handle.close() }
        var info = stat()
        guard fstat(descriptor, &info) == 0 else { throw posixError() }
        guard (info.st_mode & mode_t(S_IFMT)) == mode_t(S_IFREG),
              info.st_size >= 0, info.st_size <= Int64(limit) else { throw PersistenceError.tooLarge }
        let data = try handle.read(upToCount: limit + 1) ?? Data()
        guard data.count <= limit else { throw PersistenceError.tooLarge }
        return data
    }

    private func secureDirectory() throws {
        if try !checkDirectory() {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true,
                                                     attributes: [.posixPermissions: 0o700])
        }
        _ = try checkDirectory()
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
    }

    @discardableResult
    private func checkDirectory() throws -> Bool {
        var info = stat()
        let status = directory.path.withCString { Darwin.lstat($0, &info) }
        if status != 0 {
            if errno == ENOENT { return false }
            throw posixError()
        }
        guard (info.st_mode & mode_t(S_IFMT)) == mode_t(S_IFDIR) else {
            throw PersistenceError.invalidDirectory
        }
        return true
    }

    private func atomicWrite(_ bytes: Data, to destination: URL) throws {
        try secureDirectory()
        let temporary = directory.appendingPathComponent(".\(UUID().uuidString).tmp")
        let descriptor = temporary.path.withCString {
            Darwin.open($0, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, mode_t(0o600))
        }
        guard descriptor >= 0 else { throw posixError() }
        let handle = FileHandle(fileDescriptor: descriptor, closeOnDealloc: true)
        defer {
            try? handle.close()
            _ = temporary.path.withCString { Darwin.unlink($0) }
        }
        try handle.write(contentsOf: bytes)
        try handle.synchronize()
        try handle.close()
        let result = temporary.path.withCString { source in
            destination.path.withCString { target in Darwin.rename(source, target) }
        }
        guard result == 0 else { throw posixError() }
    }

    private func posixError() -> NSError {
        NSError(domain: NSPOSIXErrorDomain, code: Int(errno))
    }
}

private struct HistoryEnvelope: Codable {
    let version: Int
    let entries: [ClipEntry]
}

protocol HistoryKeyStore {
    func loadKey() throws -> Data?
    func saveKey(_ data: Data) throws
}

private final class KeychainHistoryKeyStore: HistoryKeyStore {
    private var query: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: "be.dollypaste.history",
         kSecAttrAccount as String: "history-aes256-v1",
         kSecAttrSynchronizable as String: false]
    }

    func loadKey() throws -> Data? {
        var query = self.query
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else {
            throw PersistenceError.keychain(status)
        }
        return data
    }

    func saveKey(_ data: Data) throws {
        var query = self.query
        query[kSecValueData as String] = data
        // Use the macOS login Keychain and its default per-app access control.
        // Data-protection Keychain requires a provisioned signing entitlement,
        // which a locally built/ad-hoc-signed app does not have. Do not claim
        // ThisDeviceOnly semantics: that attribute does not apply to this backend.
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else { throw PersistenceError.keychain(status) }
    }
}

enum PersistenceError: LocalizedError {
    case tooLarge, cannotDecrypt, cannotEncrypt, unsupportedVersion, invalidHistory
    case historyNotLoaded, invalidDirectory, keychain(OSStatus)

    var errorDescription: String? {
        switch self {
        case .tooLarge: return "The saved history exceeds Dolly Paste's storage limits."
        case .cannotDecrypt: return "The saved history cannot be decrypted with this Mac's Keychain key."
        case .cannotEncrypt: return "Dolly Paste could not encrypt the history."
        case .unsupportedVersion: return "This history was saved by an unsupported version of Dolly Paste."
        case .invalidHistory: return "The saved history contains an invalid clip."
        case .historyNotLoaded: return "Existing history could not be loaded, so saving is disabled to preserve it. Clear History to start again."
        case .invalidDirectory: return "The Dolly Paste storage location must be a regular folder."
        case .keychain(let status): return "The history encryption key is unavailable in Keychain (macOS error \(status))."
        }
    }
}
