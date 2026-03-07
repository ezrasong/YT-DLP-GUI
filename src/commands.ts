import { invoke } from "@tauri-apps/api/core";
import type { InitResult } from "./types";

export function initApp(): Promise<InitResult> {
  return invoke("init_app");
}

export function updateYtdlp(): Promise<string> {
  return invoke("update_ytdlp");
}

export function startDownload(
  id: string,
  url: string,
  format: string,
  quality: string,
  outputDir: string,
  subtitles: boolean,
  sponsorBlock: boolean,
  playlist: boolean,
): Promise<void> {
  return invoke("start_download", {
    id,
    url,
    format,
    quality,
    outputDir,
    subtitles,
    sponsorBlock,
    playlist,
  });
}

export function cancelDownload(id: string): Promise<void> {
  return invoke("cancel_download", { id });
}

export function openFolder(path: string): Promise<void> {
  return invoke("open_folder", { path });
}
