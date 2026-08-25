// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "Discord4KHelper",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Discord4KHelper", targets: ["Discord4KHelper"])
    ],
    targets: [
        .executableTarget(name: "Discord4KHelper"),
        .testTarget(name: "Discord4KHelperTests", dependencies: ["Discord4KHelper"])
    ]
)
