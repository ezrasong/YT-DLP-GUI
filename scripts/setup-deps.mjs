#!/usr/bin/env node
/**
 * Downloads yt-dlp and copies ffmpeg to src-tauri/binaries/ for sidecar bundling.
 * Run: npm run setup
 *
 * Accepts an optional target triple override via the TAURI_TARGET_TRIPLE env var
 * (used in CI for cross-compilation).
 */
import { existsSync, mkdirSync, chmodSync, copyFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { execSync } from "child_process";
import { createRequire } from "module";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = join(__dirname, "..");
const BINARIES = join(ROOT, "src-tauri", "binaries");

const platform = process.platform;
const arch = process.arch;

function triple() {
  if (platform === "darwin")
    return arch === "arm64" ? "aarch64-apple-darwin" : "x86_64-apple-darwin";
  if (platform === "win32")
    return arch === "arm64"
      ? "aarch64-pc-windows-msvc"
      : "x86_64-pc-windows-msvc";
  return arch === "arm64"
    ? "aarch64-unknown-linux-gnu"
    : "x86_64-unknown-linux-gnu";
}

const t = process.env.TAURI_TARGET_TRIPLE || triple();
const isWin = t.includes("windows");
const isMac = t.includes("apple-darwin");
const ext = isWin ? ".exe" : "";

mkdirSync(BINARIES, { recursive: true });

// ---- yt-dlp ----
const ytdlpDest = join(BINARIES, `yt-dlp-${t}${ext}`);
if (!existsSync(ytdlpDest)) {
  const url = isMac
    ? "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos"
    : isWin
      ? "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
      : "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";

  console.log(`Downloading yt-dlp for ${t}...`);
  execSync(`curl -L --progress-bar -o "${ytdlpDest}" "${url}"`, {
    stdio: "inherit",
  });
  if (!isWin) chmodSync(ytdlpDest, 0o755);
  console.log("  done.");
} else {
  console.log("yt-dlp binary already exists, skipping.");
}

// ---- ffmpeg ----
const ffmpegDest = join(BINARIES, `ffmpeg-${t}${ext}`);
if (!existsSync(ffmpegDest)) {
  try {
    const require = createRequire(import.meta.url);
    const ffmpegPath = require("ffmpeg-static");
    if (ffmpegPath && existsSync(ffmpegPath)) {
      console.log("Copying ffmpeg from ffmpeg-static...");
      copyFileSync(ffmpegPath, ffmpegDest);
      if (!isWin) chmodSync(ffmpegDest, 0o755);
      console.log("  done.");
    } else {
      console.warn(
        "ffmpeg-static binary not found. Install it: npm install -D ffmpeg-static",
      );
    }
  } catch {
    console.warn(
      "ffmpeg-static package not available. Install it: npm install -D ffmpeg-static",
    );
  }
} else {
  console.log("ffmpeg binary already exists, skipping.");
}

console.log("\nBinaries ready in src-tauri/binaries/");
