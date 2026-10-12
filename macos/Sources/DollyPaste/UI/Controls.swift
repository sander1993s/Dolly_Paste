import AppKit

/// An ordinary native button with a small closure adapter for programmatic views.
final class ActionButton: NSButton {
    var actionHandler: (() -> Void)?

    init(_ title: String, accessibilityLabel: String? = nil, action: @escaping () -> Void) {
        super.init(frame: .zero)
        self.title = title
        bezelStyle = .rounded
        controlSize = .small
        target = self
        self.action = #selector(invoke)
        actionHandler = action
        setAccessibilityLabel(accessibilityLabel ?? title)
        translatesAutoresizingMaskIntoConstraints = false
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    @objc private func invoke() { actionHandler?() }
}

final class PickerWindow: NSWindow {
    var shortcutHandler: ((NSEvent) -> Bool)?
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if shortcutHandler?(event) == true { return true }
        return super.performKeyEquivalent(with: event)
    }

    override func keyDown(with event: NSEvent) {
        if shortcutHandler?(event) != true { super.keyDown(with: event) }
    }
}

final class PickerTableView: NSTableView {
    var shortcutHandler: ((NSEvent) -> Bool)?
    override func keyDown(with event: NSEvent) {
        if shortcutHandler?(event) != true { super.keyDown(with: event) }
    }
}

/// The sheep remains crisp at every display scale without a bitmap dependency.
final class SheepView: NSView {
    private static let brandedIcon = Bundle.main.url(forResource: "DollyPaste", withExtension: "icns")
        .flatMap { NSImage(contentsOf: $0) }
    override var intrinsicContentSize: NSSize { NSSize(width: 30, height: 30) }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        if let icon = Self.brandedIcon {
            icon.draw(in: bounds, from: .zero, operation: .sourceOver, fraction: 1)
            return
        }
        NSColor.white.setFill()
        for oval in [NSRect(x: 4, y: 11, width: 16, height: 14),
                     NSRect(x: 10, y: 15, width: 13, height: 12),
                     NSRect(x: 2, y: 15, width: 12, height: 11),
                     NSRect(x: 10, y: 8, width: 13, height: 14)] {
            NSBezierPath(ovalIn: oval).fill()
        }
        NSColor(calibratedRed: 0.10, green: 0.19, blue: 0.28, alpha: 1).setFill()
        NSBezierPath(roundedRect: NSRect(x: 18, y: 12, width: 10, height: 12), xRadius: 4, yRadius: 4).fill()
        NSBezierPath(roundedRect: NSRect(x: 6, y: 4, width: 4, height: 9), xRadius: 2, yRadius: 2).fill()
        NSBezierPath(roundedRect: NSRect(x: 16, y: 4, width: 4, height: 9), xRadius: 2, yRadius: 2).fill()
        NSColor.white.setFill()
        NSBezierPath(ovalIn: NSRect(x: 24, y: 19, width: 2, height: 2)).fill()
    }
}

func label(_ text: String, size: CGFloat = 12, weight: NSFont.Weight = .regular,
           color: NSColor = .labelColor) -> NSTextField {
    let result = NSTextField(labelWithString: text)
    result.font = .systemFont(ofSize: size, weight: weight)
    result.textColor = color
    result.translatesAutoresizingMaskIntoConstraints = false
    return result
}

func horizontalStack(_ views: [NSView], spacing: CGFloat = 6) -> NSStackView {
    let stack = NSStackView(views: views)
    stack.orientation = .horizontal
    stack.spacing = spacing
    stack.alignment = .centerY
    stack.translatesAutoresizingMaskIntoConstraints = false
    return stack
}

func flexibleSpace() -> NSView {
    let view = NSView()
    view.translatesAutoresizingMaskIntoConstraints = false
    view.setContentHuggingPriority(.defaultLow, for: .horizontal)
    return view
}
