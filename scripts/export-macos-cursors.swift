import AppKit
import Foundation

// Read the host's native cursor artwork; never move or replace the desktop cursor.
// Generated PNGs remain in ignored capture/cache directories, outside the repository assets.
guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: swift export-macos-cursors.swift OUTPUT_DIRECTORY")
}
let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
NSApplication.shared.setActivationPolicy(.prohibited)
let scale: CGFloat = 2
let cursors: [(String, NSCursor)] = [
    ("Arrow", .arrow), ("Ibeam", .iBeam), ("Cross", .crosshair),
    ("Hand", .pointingHand), ("DragMove", .openHand), ("ClosedHand", .closedHand),
    ("SizeWestEast", .resizeLeftRight), ("SizeNorthSouth", .resizeUpDown),
    ("TopSide", .resizeUp), ("BottomSide", .resizeDown),
    ("LeftSide", .resizeLeft), ("RightSide", .resizeRight),
    ("DragCopy", .dragCopy), ("DragLink", .dragLink),
    ("No", .operationNotAllowed), ("Help", .contextualMenu)
]
var entries: [String: [String: Any]] = [:]
for (name, cursor) in cursors {
    let image = cursor.image
    let size = image.size
    let width = Int(ceil(size.width * scale))
    let height = Int(ceil(size.height * scale))
    guard width > 0, height > 0,
          let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width,
              pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4,
              hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
              bytesPerRow: 0, bitsPerPixel: 0),
          let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        fatalError("Could not rasterize native cursor: \(name)")
    }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    context.imageInterpolation = .high
    image.draw(in: NSRect(x: 0, y: 0, width: width, height: height),
        from: .zero, operation: .copy, fraction: 1)
    NSGraphicsContext.restoreGraphicsState()
    guard let png = bitmap.representation(using: .png, properties: [:]) else {
        fatalError("Could not encode native cursor: \(name)")
    }
    try png.write(to: output.appendingPathComponent(name + ".png"), options: .atomic)
    entries[name] = ["File": name + ".png", "Width": size.width, "Height": size.height,
        "HotspotX": cursor.hotSpot.x, "HotspotY": cursor.hotSpot.y]
}
// Match Avalonia.Native's AppKit mapping for aliases and unsupported standard shapes.
for (alias, source) in ["AppStarting": "Arrow", "Wait": "Arrow", "UpArrow": "TopSide",
    "TopLeftCorner": "Cross", "TopRightCorner": "Cross", "BottomLeftCorner": "Cross",
    "BottomRightCorner": "Cross", "SizeAll": "Cross"] {
    entries[alias] = entries[source]
}
let manifest: [String: Any] = ["Scale": scale, "Cursors": entries]
let data = try JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys])
try data.write(to: output.appendingPathComponent("cursors.json"), options: .atomic)
print("Exported \(cursors.count) native macOS cursor images at 2×, including click hotspots.")
