import AppKit
import Darwin

enum LaunchMode {
    case normal, demo, preview(URL)

    static func parse(_ arguments: [String]) throws -> LaunchMode {
        if arguments.isEmpty { return .normal }
        if arguments == ["--demo"] { return .demo }
        if arguments.count == 2, arguments[0] == "--preview", arguments[1].hasPrefix("/"),
           (arguments[1] as NSString).pathExtension.lowercased() == "png" {
            return .preview(URL(fileURLWithPath: arguments[1]))
        }
        throw NSError(domain: "DollyPaste", code: 1, userInfo: [NSLocalizedDescriptionKey:
            "Usage: DollyPaste [--demo | --preview /absolute/path/output.png]"])
    }

    var isDemo: Bool { if case .normal = self { return false }; return true }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    static let reopenNotification = Notification.Name("be.smetsoftwaresolutions.DollyPaste.show")
    private let mode: LaunchMode
    private var model: AppModel!
    private var picker: PickerController!
    private var settings: SettingsController?
    private var statusItem: NSStatusItem?
    private var hotKey: GlobalHotKey?
    private var instanceDescriptor: Int32 = -1

    init(mode: LaunchMode) { self.mode = mode }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if !mode.isDemo {
            // Advisory locking handles simultaneous launches and is released by the OS
            // on crash. O_NOFOLLOW avoids following a pre-existing lock-file symlink.
            let lockPath = NSTemporaryDirectory() + "be.smetsoftwaresolutions.DollyPaste.lock"
            instanceDescriptor = Darwin.open(lockPath, O_CREAT | O_RDWR | O_NOFOLLOW, S_IRUSR | S_IWUSR)
            guard instanceDescriptor >= 0 else { fail("Could not open the application instance lock.") }
            if flock(instanceDescriptor, LOCK_EX | LOCK_NB) != 0 {
                if errno == EWOULDBLOCK {
                    DistributedNotificationCenter.default().postNotificationName(Self.reopenNotification, object: nil,
                        userInfo: nil, deliverImmediately: true)
                    NSApp.terminate(nil)
                    return
                }
                fail("Could not lock the application instance.")
            }
            DistributedNotificationCenter.default().addObserver(self, selector: #selector(showPicker),
                name: Self.reopenNotification, object: nil)
        }
        model = AppModel(isDemo: mode.isDemo)
        picker = PickerController(model: model)
        model.onChange = { [weak self] in self?.picker.refresh(); self?.updateStatus() }
        model.onShowSettings = { [weak self] in self?.showSettings() }
        configureMenu()
        if !mode.isDemo {
            let shortcut = GlobalHotKey { [weak self] in self?.togglePicker() }
            hotKey = shortcut
            do { try shortcut.register() }
            catch { model.shortcutWarning = "⌘⇧V is unavailable. Open Dolly Paste from the menu bar. \(error.localizedDescription)" }
            model.start()
        }
        showPicker()
        if case .preview(let output) = mode {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in self?.renderPreview(to: output) }
        }
    }

    private func configureMenu() {
        // The Edit menu supplies standard native editing shortcuts in text fields.
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Quit Dolly Paste", action: #selector(quit), keyEquivalent: "q").target = self
        appItem.submenu = appMenu
        let editItem = NSMenuItem()
        main.addItem(editItem)
        let editMenu = NSMenu(title: "Edit")
        for (title, action, key) in [("Cut", "cut:", "x"), ("Copy", "copy:", "c"),
                                     ("Paste", "paste:", "v"), ("Select All", "selectAll:", "a")] {
            editMenu.addItem(withTitle: title, action: Selector(action), keyEquivalent: key)
        }
        editItem.submenu = editMenu
        NSApp.mainMenu = main
        guard !mode.isDemo else { return }
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem = item
        if let url = Bundle.main.url(forResource: "DollyPaste", withExtension: "icns"),
           let image = NSImage(contentsOf: url) {
            image.size = NSSize(width: 22, height: 22)
            item.button?.image = image
        } else {
            item.button?.title = "🐑"
        }
        item.button?.toolTip = "Dolly Paste — ⌘⇧V"
        item.button?.setAccessibilityLabel("Dolly Paste clipboard history")
        item.button?.target = self
        item.button?.action = #selector(statusClicked)
        item.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
    }

    private func updateStatus() { statusItem?.button?.toolTip = model.isPaused ? "Dolly Paste — recording paused" : "Dolly Paste — ⌘⇧V" }

    @objc private func statusClicked() {
        if NSApp.currentEvent?.type == .rightMouseUp {
            let menu = NSMenu()
            menu.addItem(withTitle: "Open Dolly Paste", action: #selector(showPicker), keyEquivalent: "").target = self
            menu.addItem(withTitle: model.isPaused ? "Resume recording" : "Pause recording", action: #selector(togglePause), keyEquivalent: "").target = self
            menu.addItem(withTitle: "Settings…", action: #selector(showSettings), keyEquivalent: "").target = self
            menu.addItem(.separator())
            menu.addItem(withTitle: "Quit Dolly Paste", action: #selector(quit), keyEquivalent: "q").target = self
            if let button = statusItem?.button { menu.popUp(positioning: nil, at: NSPoint(x: 0, y: button.bounds.height), in: button) }
        } else { togglePicker() }
    }

    private func togglePicker() {
        if picker.window?.isVisible == true { picker.hide() } else { showPicker() }
    }
    @objc private func showPicker() { picker?.showNearPointer() }
    @objc private func togglePause() { model.togglePause() }
    @objc private func quit() { NSApp.terminate(nil) }
    @objc private func showSettings() {
        if settings == nil { settings = SettingsController(model: model) }
        settings?.show()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showPicker(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) {
        model?.stop()
        hotKey?.unregister()
        DistributedNotificationCenter.default().removeObserver(self)
        if instanceDescriptor >= 0 { Darwin.close(instanceDescriptor) }
    }

    private func renderPreview(to output: URL) {
        do {
            guard let view = picker.window?.contentView else { throw previewError() }
            view.layoutSubtreeIfNeeded()
            guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw previewError() }
            view.cacheDisplay(in: view.bounds, to: bitmap)
            guard let png = bitmap.representation(using: .png, properties: [:]), !png.isEmpty else { throw previewError() }
            try png.write(to: output, options: .atomic)
            NSApp.terminate(nil)
        } catch { fail("Preview failed: \(error.localizedDescription)") }
    }
    private func previewError() -> Error { NSError(domain: "DollyPaste", code: 2, userInfo: [NSLocalizedDescriptionKey: "Could not render the picker."]) }
    private func fail(_ message: String) -> Never {
        FileHandle.standardError.write(Data((message + "\n").utf8))
        Darwin.exit(1)
    }
}

do {
    let mode = try LaunchMode.parse(Array(CommandLine.arguments.dropFirst()))
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    let delegate = AppDelegate(mode: mode)
    app.delegate = delegate
    withExtendedLifetime(delegate) { app.run() }
} catch {
    FileHandle.standardError.write(Data((error.localizedDescription + "\n").utf8))
    Darwin.exit(1)
}
