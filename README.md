# YT-DLP GUI

A modern, cross-platform desktop GUI for [yt-dlp](https://github.com/yt-dlp/yt-dlp) built with **Kotlin** and **Compose Multiplatform**. Features a OneUI-inspired design with a clean, rounded interface.

![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-blue)
![Kotlin](https://img.shields.io/badge/kotlin-2.0-purple)

## Features

- **Queue-based downloads** with real-time progress, speed, and ETA
- **Format selection** — Video (MP4, MKV, WEBM) or Audio (MP3, M4A, FLAC, WAV, OPUS)
- **Quality selection** — Best, 4K, 1440p, 1080p, 720p, 480p, 360p (video) / bitrate options (audio)
- **SponsorBlock** — automatically remove sponsored segments
- **Subtitle downloads** — embed subtitles in all available languages
- **Playlist support** — download full playlists or single videos
- **Concurrent downloads** — configurable parallel download limit
- **yt-dlp management** — auto-install, version checking, and one-click updates
- **Dark / Light / System theme** — OneUI-inspired design
- **Cross-platform** — single codebase for Windows, macOS, and Linux

## Download

Grab the latest release from the [Releases](../../releases) page:

| Platform | Installer | Portable |
|----------|-----------|----------|
| Windows  | `.msi`    | `.zip`   |
| macOS x64 | `.dmg`   | `.tar.gz` |
| macOS ARM | `.dmg`   | `.tar.gz` |
| Linux    | `.deb`    | `.tar.gz` |

## Build from Source

### Prerequisites

- **JDK 17+** (recommended: [Adoptium Temurin](https://adoptium.net/))
- **Gradle 8.10+** (or use the wrapper)

### Generate Gradle Wrapper (first time only)

If the repository doesn't include `gradlew` scripts:

```bash
gradle wrapper
```

### Run in development

```bash
./gradlew run
```

### Build native packages

```bash
# Windows (.msi)
./gradlew packageMsi

# macOS (.dmg)
./gradlew packageDmg

# Linux (.deb)
./gradlew packageDeb

# Portable directory (all platforms)
./gradlew createDistributable
```

## Project Structure

```
├── build.gradle.kts              # Build configuration
├── src/main/kotlin/com/ytdlpgui/
│   ├── Main.kt                   # Application entry point
│   ├── theme/
│   │   └── Theme.kt              # OneUI-inspired Material 3 theme
│   ├── model/
│   │   └── Models.kt             # Data models and enums
│   ├── service/
│   │   ├── YtDlpService.kt       # yt-dlp binary management
│   │   └── DownloadManager.kt    # Queue and state management
│   └── ui/
│       ├── App.kt                # Root layout with sidebar navigation
│       ├── Components.kt         # Reusable UI components
│       ├── DownloadScreen.kt     # Main download view
│       └── SettingsScreen.kt     # Settings and yt-dlp management
```

## Tech Stack

- **Language:** Kotlin 2.0
- **UI Framework:** Compose Multiplatform 1.7 (Material 3)
- **Async:** Kotlin Coroutines
- **Packaging:** Compose native distributions (MSI, DMG, DEB)

## License

MIT
