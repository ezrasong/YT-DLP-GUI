# YT-DLP GUI

A modern desktop GUI for [yt-dlp](https://github.com/yt-dlp/yt-dlp). Single executable, no installer needed. Built with C# and Avalonia UI.

## Download

Grab the latest from [Releases](../../releases):

| Platform | File |
|----------|------|
| Windows | `yt-dlp-gui-windows-x64.exe` |
| macOS Intel | `yt-dlp-gui-macos-x64` |
| macOS Apple Silicon | `yt-dlp-gui-macos-arm64` |
| Linux | `yt-dlp-gui-linux-x64` |

**One file. No installer. yt-dlp and ffmpeg bundled inside.**

## Features

- Queue-based downloads with real-time progress, speed, and ETA
- Format selection — Video (MP4, MKV, WEBM) or Audio (MP3, M4A, FLAC, WAV, OPUS)
- Quality selection — Best, 4K, 1440p, 1080p, 720p, 480p (video) / bitrate (audio)
- SponsorBlock — remove sponsored segments
- Subtitle downloads
- Playlist support
- yt-dlp auto-install, version check, one-click update
- Dark theme

## Build from Source

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run

```bash
dotnet run --project src/YtDlpGui.csproj
```

### Publish single exe

```bash
# Windows
dotnet publish src/YtDlpGui.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true

# macOS
dotnet publish src/YtDlpGui.csproj -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true

# Linux
dotnet publish src/YtDlpGui.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Output is in `src/bin/Release/net8.0/<rid>/publish/`.

## License

MIT
