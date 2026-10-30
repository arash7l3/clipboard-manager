// swift-tools-version:5.9
// Smoke test for the packaged core. Run with `cargo xtask smoke swift`.

import PackageDescription

let package = Package(
    name: "ClipoSmoke",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(path: "../../../dist/apple/ClipoCore")
    ],
    targets: [
        .executableTarget(
            name: "ClipoSmoke",
            dependencies: [.product(name: "ClipoCore", package: "ClipoCore")]
        )
    ]
)
