import Carbon
import Foundation

/// Carbon hotkey registration observes only Cmd-Shift-V and needs no Accessibility access.
final class GlobalHotKey {
    private let action: () -> Void
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?
    private let signature: OSType = 0x444C5054 // DLPT
    private let identifier: UInt32 = 1

    init(action: @escaping () -> Void) { self.action = action }

    func register() throws {
        precondition(Thread.isMainThread)
        guard hotKey == nil else { return }
        var eventType = EventTypeSpec(eventClass: OSType(kEventClassKeyboard),
                                      eventKind: UInt32(kEventHotKeyPressed))
        let installStatus = InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let context, let event else { return OSStatus(eventNotHandledErr) }
            let owner = Unmanaged<GlobalHotKey>.fromOpaque(context).takeUnretainedValue()
            var received = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                EventParamType(typeEventHotKeyID), nil, MemoryLayout<EventHotKeyID>.size,
                nil, &received)
            guard status == noErr, received.signature == owner.signature,
                  received.id == owner.identifier else { return OSStatus(eventNotHandledErr) }
            // Carbon invokes this handler on the main event loop. Retain owner
            // through the callback and avoid enqueuing an unretained pointer.
            owner.action()
            return noErr
        }, 1, &eventType, Unmanaged.passUnretained(self).toOpaque(), &handler)
        guard installStatus == noErr else { throw HotKeyError(status: installStatus) }

        let keyID = EventHotKeyID(signature: signature, id: identifier)
        let status = RegisterEventHotKey(UInt32(kVK_ANSI_V), UInt32(cmdKey | shiftKey),
                                         keyID, GetApplicationEventTarget(), 0, &hotKey)
        guard status == noErr else {
            unregister()
            throw HotKeyError(status: status)
        }
    }

    func unregister() {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = nil
        if let handler { RemoveEventHandler(handler) }
        handler = nil
    }

    deinit { unregister() }
}

struct HotKeyError: LocalizedError {
    let status: OSStatus
    var errorDescription: String? {
        "Cmd-Shift-V could not be registered (macOS error \(status)). Another app may use it. Open Dolly Paste from the menu bar instead."
    }
}
