import AppKit

// Vector geometry adapted from assets/sheep.svg; render all native icon sizes.
guard CommandLine.arguments.count == 2 else { fatalError("Expected an iconset directory") }
let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
func color(_ hex: UInt32) -> NSColor {
    NSColor(calibratedRed: CGFloat((hex >> 16) & 255) / 255,
            green: CGFloat((hex >> 8) & 255) / 255, blue: CGFloat(hex & 255) / 255, alpha: 1)
}
func ellipse(_ x: CGFloat, _ y: CGFloat, _ rx: CGFloat, _ ry: CGFloat, _ fill: UInt32, _ stroke: UInt32? = nil) {
    let path = NSBezierPath(ovalIn: NSRect(x: x-rx, y: y-ry, width: rx*2, height: ry*2))
    color(fill).setFill(); path.fill()
    if let stroke { color(stroke).setStroke(); path.lineWidth = 1; path.stroke() }
}
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
        let transform = NSAffineTransform()
        transform.scale(by: CGFloat(pixels)/100)
        transform.concat()
        color(0x086ECB).setFill()
        NSBezierPath(roundedRect: NSRect(x: 4, y: 4, width: 92, height: 92), xRadius: 21, yRadius: 21).fill()
        // Flip to the SVG coordinate system.
        let flip = NSAffineTransform()
        flip.translateX(by: 0, yBy: 100); flip.scaleX(by: 1, yBy: -1); flip.concat()
        color(0x202428).setFill()
        for x in [CGFloat](arrayLiteral: 33, 42, 52, 61) {
            NSBezierPath(roundedRect: NSRect(x: x, y: 74, width: 6, height: 14), xRadius: 3, yRadius: 3).fill()
        }
        let lobes: [(CGFloat, CGFloat, CGFloat)] = [(34,54,14),(66,54,14),(40,42,13),(60,42,13),(36,66,12),(64,66,12),(50,68,14),(50,52,17)]
        for (x, y, radius) in lobes {
            ellipse(x, y, radius, radius, 0xFAF8F5, 0xDDD8CE)
        }
        for reflected in [false, true] {
            NSGraphicsContext.saveGraphicsState()
            if reflected { let t = NSAffineTransform(); t.translateX(by: 100, yBy: 0); t.scaleX(by: -1, yBy: 1); t.concat() }
            let horn = NSBezierPath()
            horn.move(to: NSPoint(x: 40, y: 42))
            horn.curve(to: NSPoint(x: 12, y: 52), controlPoint1: NSPoint(x: 26, y: 30), controlPoint2: NSPoint(x: 10, y: 38))
            horn.curve(to: NSPoint(x: 28, y: 54), controlPoint1: NSPoint(x: 14, y: 62), controlPoint2: NSPoint(x: 26, y: 62))
            horn.curve(to: NSPoint(x: 20, y: 51), controlPoint1: NSPoint(x: 29, y: 48), controlPoint2: NSPoint(x: 22, y: 47))
            horn.lineWidth = 5; horn.lineCapStyle = .round
            color(0xCEAC70).setStroke(); horn.stroke()
            ellipse(32, 48, 4, 2.5, 0xEADBC8, 0xD3C1AD)
            NSGraphicsContext.restoreGraphicsState()
        }
        ellipse(50, 54, 13, 15, 0xEADBC8, 0xD3C1AD)
        let crown: [(CGFloat, CGFloat, CGFloat)] = [(44,38,5),(56,38,5),(50,35,5.5)]
        for (x,y,r) in crown { ellipse(x,y,r,r,0xFAF8F5,0xDDD8CE) }
        for x in [CGFloat](arrayLiteral: 44.5,55.5) { ellipse(x,51,1.8,1.8,0x202428); ellipse(x+0.6,50.4,0.6,0.6,0xFFFFFF) }
        let mouth = NSBezierPath()
        mouth.move(to: NSPoint(x: 48.5, y: 58))
        mouth.curve(to: NSPoint(x: 51.5, y: 58), controlPoint1: NSPoint(x: 49, y: 59), controlPoint2: NSPoint(x: 51, y: 59))
        mouth.move(to: NSPoint(x: 50, y: 56.5)); mouth.line(to: NSPoint(x: 50, y: 58.5))
        mouth.lineWidth = 1.2; color(0x8E7864).setStroke(); mouth.stroke()
        NSGraphicsContext.restoreGraphicsState()
        let suffix = scale == 2 ? "@2x" : ""
        let png = bitmap.representation(using: .png, properties: [:])!
        try png.write(to: directory.appendingPathComponent("icon_\(size)x\(size)\(suffix).png"))
    }
}
