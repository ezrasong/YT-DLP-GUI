# Shared Native Contract

This contract keeps WPF and SwiftUI behavior aligned.

## Download Status Values

- `queued`
- `fetching`
- `downloading`
- `processing`
- `finished`
- `error`
- `cancelled`

## Update Status Shape

- `installed: bool`
- `current_version: string`
- `latest_version: string`
- `update_available: bool`
- `error: string` (empty string when no error)

## Download Progress Shape

- `id: string`
- `percent: number` (0 to 100)
- `speed: string`
- `eta: string`
- `status: string` (one of status values above)
- `title: string`
- `error: string`

## Core Behavior Rules

- Startup checks must be asynchronous and never freeze first render.
- Update flow must support both first-time install and update.
- UI must show explicit states:
  - `yt-dlp missing`
  - `checking`
  - `update available`
  - `up to date`
  - `could not verify latest version`
- Update button labels:
  - `Install yt-dlp` when missing
  - `Update yt-dlp` when update is available
  - `Up to date` when no update is required (disabled)
- Download errors should surface the last `ERROR:` line from yt-dlp when available.

