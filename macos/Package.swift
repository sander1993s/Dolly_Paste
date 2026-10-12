// swift-tools-version: 5.9
import PackageDescription

var products: [Product] = [.library(name: "DollyPasteCore", targets: ["DollyPasteCore"])]
var targets: [Target] = [
    .target(name: "DollyPasteCore"),
    .testTarget(name: "DollyPasteCoreTests", dependencies: ["DollyPasteCore"])
]
#if os(macOS)
products.append(.executable(name: "DollyPaste", targets: ["DollyPaste"]))
targets.append(.executableTarget(name: "DollyPaste", dependencies: ["DollyPasteCore"],
    linkerSettings: [.linkedFramework("AppKit"), .linkedFramework("Carbon"),
                     .linkedFramework("Vision"), .linkedFramework("Security")]))
targets.append(.testTarget(name: "DollyPastePlatformTests", dependencies: ["DollyPaste", "DollyPasteCore"]))
#endif

let package = Package(name: "DollyPaste", platforms: [.macOS(.v13)],
                      products: products, targets: targets, swiftLanguageVersions: [.v5])
