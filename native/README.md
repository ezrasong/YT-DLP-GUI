# Native Migration Workspace

This folder contains the native app targets:

- `windows/YtDlpGui.Wpf`: WPF desktop app (.NET 8, Windows 10/11)
- `macos/YtDlpNativeMac`: SwiftUI macOS app (Swift Package layout)
- `shared/BackendContract.md`: shared behavior and state contract

## Scope

- Native UI for both Windows and macOS
- Queue-based downloads with per-item progress and cancel
- yt-dlp install/update flow with clear status
- Non-blocking startup/update checks

## Build: Windows (WPF)

1. Open `native/windows/YtDlpGui.Wpf.sln` in Visual Studio 2022.
2. Build and run the `YtDlpGui.Wpf` project.

You can also run:

```powershell
dotnet publish native/windows/YtDlpGui.Wpf/YtDlpGui.Wpf.csproj -c Release -r win-x64 --self-contained true -o native-artifacts/windows
```

## Build: macOS (SwiftUI)

1. On macOS, open `native/macos/YtDlpNativeMac` in Xcode (or use `swift build` first).
2. Build and run the `YtDlpNativeMac` executable target.

## Notes

- Both native apps keep yt-dlp in app-specific writable data directories.
- Latest-version checks use GitHub Releases API.
- The two frontends intentionally use the same status terms (`queued`, `fetching`, `downloading`, `processing`, `finished`, `error`, `cancelled`) so behavior stays aligned.
