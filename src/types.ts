export type DownloadStatus =
  | "queued"
  | "fetching"
  | "downloading"
  | "processing"
  | "finished"
  | "error"
  | "cancelled";

export interface DownloadJob {
  id: string;
  url: string;
  title: string;
  format: string;
  quality: string;
  outputDir: string;
  subtitles: boolean;
  sponsorBlock: boolean;
  playlist: boolean;
  status: DownloadStatus;
  progress: number;
  speed: string;
  eta: string;
  error: string;
}

export interface ProgressEvent {
  id: string;
  percent: number;
  speed: string;
  eta: string;
  status: string;
  title: string;
  error: string;
}

export interface InitResult {
  ytdlp: boolean;
  ffmpeg: boolean;
  ytdlp_version: string;
  download_dir: string;
}
