// swift-tools-version:5.9
// Template for the Swift package that `cargo xtask package macos` writes to
// core/dist/apple/ClipoCore. Add that folder to the Xcode project as a local
// package and `import ClipoCore`.

import PackageDescription

let package = Package(
    name: "ClipoCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "ClipoCore", targets: ["ClipoCore"])
    ],
    targets: [
        // Universal static library with the C header and module map.
        .binaryTarget(name: "ClipoCoreFFI", path: "ClipoCoreFFI.xcframework"),
        // Generated Swift API over it.
        .target(
            name: "ClipoCore",
            dependencies: ["ClipoCoreFFI"],
            // SQLCipher's CommonCrypto backend calls into Security.framework.
            linkerSettings: [.linkedFramework("Security")]
        )
    ]
)
