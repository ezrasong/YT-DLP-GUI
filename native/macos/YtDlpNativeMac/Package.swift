// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "YtDlpNativeMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "YtDlpNativeMac", targets: ["YtDlpNativeMac"]),
    ],
    targets: [
        .executableTarget(name: "YtDlpNativeMac"),
    ]
)
