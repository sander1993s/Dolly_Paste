import XCTest
import AppKit
import DollyPasteCore
@testable import DollyPaste

/// These tests use the actual native controls and synthetic history only. They
/// never start capture, touch the general pasteboard, or access the Keychain.
final class PickerTests: XCTestCase {
    private func onMain(_ body: () throws -> Void) rethrows {
        if Thread.isMainThread { try body() } else { try DispatchQueue.main.sync(execute: body) }
    }

    private func makePicker() -> (AppModel, PickerController) {
        _ = NSApplication.shared
        let model = AppModel(isDemo: true)
        let picker = PickerController(model: model)
        model.onChange = { [weak picker] in picker?.refresh() }
        return (model, picker)
    }

    private func descendants(of view: NSView) -> [NSView] {
        [view] + view.subviews.flatMap { descendants(of: $0) }
    }

    private func view<T: NSView>(_ id: String, in root: NSView, as type: T.Type = T.self) throws -> T {
        try XCTUnwrap(descendants(of: root).first { $0.identifier?.rawValue == id } as? T,
                      "Missing native control \(id)")
    }

    private func sendAction(_ control: NSControl) throws {
        let action = try XCTUnwrap(control.action)
        XCTAssertTrue(NSApp.sendAction(action, to: control.target, from: control))
    }

    func testDefaultAndMinimumLayoutsRenderWithExpandedImagePreview() throws {
        try onMain {
            let (_, picker) = makePicker()
            defer { picker.hide() }
            let window = try XCTUnwrap(picker.window)
            let root = try XCTUnwrap(window.contentView)
            let toggle: NSButton = try view("history.preview-toggle", in: root)
            toggle.performClick(nil)
            for size in [NSSize(width: 480, height: 520), NSSize(width: 420, height: 460)] {
                window.setContentSize(size)
                root.layoutSubtreeIfNeeded()
                for id in ["history.search", "history.scope", "history.kinds", "history.copy", "history.pin",
                           "history.delete", "history.preview-toggle", "history.copy-ocr", "history.copy-base64",
                           "history.pause", "history.settings", "history.clear"] {
                    let control: NSView = try view(id, in: root)
                    let frame = control.convert(control.bounds, to: root)
                    XCTAssertGreaterThan(frame.width, 0, id)
                    XCTAssertGreaterThan(frame.height, 0, id)
                    XCTAssertTrue(root.bounds.insetBy(dx: -1, dy: -1).contains(frame),
                                  "\(id) is clipped at \(size): \(frame)")
                    XCTAssertFalse(control.isHidden, id)
                }
                let table: NSTableView = try view("history.list", in: root)
                let scroller = try XCTUnwrap(table.enclosingScrollView)
                XCTAssertGreaterThanOrEqual(scroller.frame.height, 61)
                let bitmap = try XCTUnwrap(root.bitmapImageRepForCachingDisplay(in: root.bounds))
                root.cacheDisplay(in: root.bounds, to: bitmap)
                let png = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
                XCTAssertGreaterThan(png.count, 1_000, "The native picker should render visible content")
            }
        }
    }

    func testReopeningResetsSearchFiltersAndExpandedPreview() throws {
        try onMain {
            let (model, picker) = makePicker()
            defer { picker.hide() }
            picker.showNearPointer()
            let root = try XCTUnwrap(picker.window?.contentView)
            let search: NSSearchField = try view("history.search", in: root)
            let scope: NSSegmentedControl = try view("history.scope", in: root)
            let kinds: NSSegmentedControl = try view("history.kinds", in: root)
            let toggle: NSButton = try view("history.preview-toggle", in: root)
            let table: NSTableView = try view("history.list", in: root)
            toggle.performClick(nil)
            let preview: NSView = try view("history.preview", in: root)
            XCTAssertFalse(preview.isHidden)
            search.stringValue = "meeting"
            picker.controlTextDidChange(Notification(name: NSControl.textDidChangeNotification, object: search))
            scope.selectedSegment = 1
            try sendAction(scope)
            kinds.selectedSegment = 1
            try sendAction(kinds)
            XCTAssertEqual(table.numberOfRows, 1)
            picker.hide()
            picker.showNearPointer()
            XCTAssertEqual(search.stringValue, "")
            XCTAssertEqual(scope.selectedSegment, 0)
            XCTAssertEqual(kinds.selectedSegment, 0)
            XCTAssertTrue(preview.isHidden)
            XCTAssertEqual(table.numberOfRows, model.history.entries.count)
            let copy: NSButton = try view("history.copy", in: root)
            XCTAssertEqual(copy.title, "Copy Image", "A reopened image starts with its native format")
        }
    }

    func testImageFormatNavigationPreservesSearchCaretAndHistoryOrder() throws {
        try onMain {
            let (model, picker) = makePicker()
            defer { picker.hide() }
            picker.showNearPointer()
            let root = try XCTUnwrap(picker.window?.contentView)
            let search: NSSearchField = try view("history.search", in: root)
            let copy: NSButton = try view("history.copy", in: root)
            search.stringValue = "Dolly"
            picker.controlTextDidChange(Notification(name: NSControl.textDidChangeNotification, object: search))
            picker.window?.makeFirstResponder(search)
            let editor = try XCTUnwrap(search.currentEditor() as? NSTextView)
            editor.setSelectedRange(NSRange(location: 2, length: 0))
            XCTAssertFalse(picker.control(search, textView: editor, doCommandBy: Selector("moveRight:")),
                           "Arrow keys inside a search term must keep ordinary caret movement")
            XCTAssertEqual(copy.title, "Copy Image")
            editor.setSelectedRange(NSRange(location: 5, length: 0))
            XCTAssertTrue(picker.control(search, textView: editor, doCommandBy: Selector("moveRight:")))
            XCTAssertEqual(copy.title, "Copy Text")
            let order = model.history.entries.map(\.id)
            let pin: NSButton = try view("history.pin", in: root)
            pin.performClick(nil)
            XCTAssertEqual(model.history.entries.map(\.id), order, "Pinning must preserve keyboard row order")
            XCTAssertEqual(copy.title, "Copy Text", "Updating the same image preserves the chosen format")
        }
    }
}
