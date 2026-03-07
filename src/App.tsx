import { useState, useEffect, useCallback, useRef } from "react";
import { listen } from "@tauri-apps/api/event";
import type { DownloadJob, ProgressEvent, InitResult, DownloadStatus } from "./types";
import {
  initApp,
  updateYtdlp,
  startDownload,
  cancelDownload,
  openFolder,
} from "./commands";

const VIDEO_FORMATS = ["Video (MP4)", "Video (MKV)", "Video (WEBM)"];
const AUDIO_FORMATS = [
  "Audio (MP3)",
  "Audio (M4A)",
  "Audio (FLAC)",
  "Audio (WAV)",
  "Audio (OPUS)",
];
const VIDEO_QUALITIES = ["Best", "4K", "1440p", "1080p", "720p", "480p", "360p"];
const AUDIO_QUALITIES = ["Best", "320kbps", "256kbps", "192kbps", "128kbps"];

const ACTIVE_STATUSES: DownloadStatus[] = ["fetching", "downloading", "processing"];

function App() {
  const [url, setUrl] = useState("");
  const [format, setFormat] = useState("Video (MP4)");
  const [quality, setQuality] = useState("Best");
  const [outputDir, setOutputDir] = useState("");
  const [subtitles, setSubtitles] = useState(false);
  const [sponsorBlock, setSponsorBlock] = useState(false);
  const [playlist, setPlaylist] = useState(false);
  const [jobs, setJobs] = useState<DownloadJob[]>([]);
  const [init, setInit] = useState<InitResult | null>(null);
  const [updating, setUpdating] = useState(false);
  const nextId = useRef(0);

  const isAudio = format.startsWith("Audio");
  const qualities = isAudio ? AUDIO_QUALITIES : VIDEO_QUALITIES;

  // Init: check deps + get download dir
  useEffect(() => {
    initApp()
      .then((result) => {
        setInit(result);
        setOutputDir(result.download_dir);
      })
      .catch(() => {});
  }, []);

  // Listen for progress events from Rust
  useEffect(() => {
    const unlisten = listen<ProgressEvent>("download-progress", (event) => {
      const p = event.payload;
      setJobs((prev) =>
        prev.map((j) =>
          j.id === p.id
            ? {
                ...j,
                title: p.title || j.title,
                progress: p.percent,
                speed: p.speed,
                eta: p.eta,
                status: p.status as DownloadStatus,
                error: p.error || j.error,
              }
            : j,
        ),
      );
    });
    return () => {
      unlisten.then((fn) => fn());
    };
  }, []);

  // Reset quality when format type changes
  useEffect(() => {
    setQuality("Best");
  }, [isAudio]);

  const addToQueue = useCallback(() => {
    const trimmed = url.trim();
    if (!trimmed || !outputDir) return;
    nextId.current += 1;
    const id = `dl-${nextId.current}`;
    const job: DownloadJob = {
      id,
      url: trimmed,
      title: trimmed,
      format,
      quality,
      outputDir,
      subtitles,
      sponsorBlock,
      playlist,
      status: "queued",
      progress: 0,
      speed: "",
      eta: "",
      error: "",
    };
    setJobs((prev) => [...prev, job]);
    setUrl("");
  }, [url, format, quality, outputDir, subtitles, sponsorBlock, playlist]);

  const downloadAll = useCallback(() => {
    for (const j of jobs) {
      if (j.status === "queued") {
        startDownload(
          j.id,
          j.url,
          j.format,
          j.quality,
          j.outputDir,
          j.subtitles,
          j.sponsorBlock,
          j.playlist,
        ).catch((err: unknown) => {
          setJobs((prev) =>
            prev.map((pj) =>
              pj.id === j.id ? { ...pj, status: "error" as const, error: String(err) } : pj,
            ),
          );
        });
      }
    }
  }, [jobs]);

  const handleCancel = useCallback((id: string) => {
    cancelDownload(id).catch(() => {});
    setJobs((prev) =>
      prev.map((j) =>
        j.id === id && (j.status === "queued" || ACTIVE_STATUSES.includes(j.status))
          ? { ...j, status: "cancelled" as const }
          : j,
      ),
    );
  }, []);

  const cancelAll = useCallback(() => {
    for (const j of jobs) {
      if (j.status === "queued" || ACTIVE_STATUSES.includes(j.status)) {
        handleCancel(j.id);
      }
    }
  }, [jobs, handleCancel]);

  const removeJob = useCallback((id: string) => {
    setJobs((prev) => prev.filter((j) => j.id !== id));
  }, []);

  const clearDone = useCallback(() => {
    setJobs((prev) =>
      prev.filter((j) => !["finished", "error", "cancelled"].includes(j.status)),
    );
  }, []);

  const handlePaste = useCallback(async () => {
    try {
      const text = await navigator.clipboard.readText();
      setUrl(text.trim());
    } catch {
      /* clipboard not available */
    }
  }, []);

  const handleUpdate = useCallback(async () => {
    setUpdating(true);
    try {
      const version = await updateYtdlp();
      setInit((prev) => (prev ? { ...prev, ytdlp: true, ytdlp_version: version } : prev));
    } catch {
      /* update failed */
    }
    setUpdating(false);
  }, []);

  // Stats
  const active = jobs.filter((j) => ACTIVE_STATUSES.includes(j.status)).length;
  const queued = jobs.filter((j) => j.status === "queued").length;
  const done = jobs.filter((j) => j.status === "finished").length;
  const failed = jobs.filter((j) => j.status === "error").length;

  return (
    <div className="h-screen flex flex-col bg-black text-white">
      {/* ---- Header ---- */}
      <header className="px-5 pt-6 pb-3 shrink-0">
        <div className="flex items-center justify-between mb-1">
          <h1 className="text-[26px] font-bold tracking-tight">yt-dlp GUI</h1>
          {init?.ytdlp && (
            <button
              onClick={handleUpdate}
              disabled={updating}
              className="px-3.5 py-1.5 rounded-full text-xs font-medium bg-[#1c1c1e]
                         text-[#8e8e93] hover:bg-[#2c2c2e] transition-colors disabled:opacity-50"
            >
              {updating ? "Updating..." : "Update yt-dlp"}
            </button>
          )}
        </div>
        {init && (
          <div className="flex gap-3 text-xs">
            <span className={init.ytdlp ? "text-[#30d158]" : "text-[#ff453a]"}>
              {init.ytdlp ? `yt-dlp ${init.ytdlp_version}` : "yt-dlp missing"}
            </span>
            <span className={init.ffmpeg ? "text-[#30d158]" : "text-[#ff9f0a]"}>
              {init.ffmpeg ? "ffmpeg" : "ffmpeg missing"}
            </span>
          </div>
        )}
      </header>

      {/* ---- URL Input ---- */}
      <section className="px-5 py-2 shrink-0">
        <div className="bg-[#1c1c1e] rounded-[20px] p-5">
          <label className="text-sm font-medium text-[#8e8e93] mb-2.5 block">
            Video / Playlist URL
          </label>
          <div className="flex gap-2">
            <input
              className="flex-1 bg-[#2c2c2e] text-white rounded-full px-5 py-3 text-sm
                         outline-none border-none focus:ring-2 focus:ring-[#3478f6]/50
                         transition-all placeholder:text-[#48484a]"
              placeholder="Paste a URL here..."
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && addToQueue()}
            />
            <button
              onClick={handlePaste}
              className="px-4 py-3 bg-[#2c2c2e] hover:bg-[#3a3a3c] rounded-full
                         text-sm font-medium transition-colors text-[#8e8e93]"
            >
              Paste
            </button>
            <button
              onClick={addToQueue}
              className="px-6 py-3 bg-[#3478f6] hover:bg-[#2c6ade] rounded-full text-sm
                         font-semibold transition-colors"
            >
              Add
            </button>
          </div>
        </div>
      </section>

      {/* ---- Options ---- */}
      <section className="px-5 py-2 shrink-0">
        <div className="bg-[#1c1c1e] rounded-[20px] p-5">
          <div className="grid grid-cols-2 gap-4 mb-5">
            {/* Format */}
            <div>
              <label className="text-xs font-medium text-[#8e8e93] mb-2 block">Format</label>
              <select
                value={format}
                onChange={(e) => setFormat(e.target.value)}
                className="w-full bg-[#2c2c2e] border-none rounded-2xl px-4 py-2.5
                           text-sm outline-none focus:ring-2 focus:ring-[#3478f6]/50
                           transition-all text-white"
              >
                <optgroup label="Video">
                  {VIDEO_FORMATS.map((f) => (
                    <option key={f}>{f}</option>
                  ))}
                </optgroup>
                <optgroup label="Audio">
                  {AUDIO_FORMATS.map((f) => (
                    <option key={f}>{f}</option>
                  ))}
                </optgroup>
              </select>
            </div>

            {/* Quality */}
            <div>
              <label className="text-xs font-medium text-[#8e8e93] mb-2 block">Quality</label>
              <select
                value={quality}
                onChange={(e) => setQuality(e.target.value)}
                className="w-full bg-[#2c2c2e] border-none rounded-2xl px-4 py-2.5
                           text-sm outline-none focus:ring-2 focus:ring-[#3478f6]/50
                           transition-all text-white"
              >
                {qualities.map((q) => (
                  <option key={q}>{q}</option>
                ))}
              </select>
            </div>
          </div>

          {/* Save to */}
          <div className="mb-5">
            <label className="text-xs font-medium text-[#8e8e93] mb-2 block">Save to</label>
            <input
              value={outputDir}
              onChange={(e) => setOutputDir(e.target.value)}
              className="w-full bg-[#2c2c2e] border-none rounded-2xl px-4 py-2.5
                         text-sm outline-none focus:ring-2 focus:ring-[#3478f6]/50
                         transition-all text-white"
            />
          </div>

          {/* Toggle switches */}
          <div className="space-y-3">
            <ToggleOption label="Subtitles" checked={subtitles} onChange={setSubtitles} />
            <ToggleOption label="SponsorBlock" checked={sponsorBlock} onChange={setSponsorBlock} />
            <ToggleOption label="Full Playlist" checked={playlist} onChange={setPlaylist} />
          </div>
        </div>
      </section>

      {/* ---- Queue Header ---- */}
      <div className="flex items-center justify-between px-5 pt-4 pb-2 shrink-0">
        <h2 className="text-lg font-semibold">
          Queue{" "}
          {jobs.length > 0 && (
            <span className="text-[#48484a] font-normal text-base">({jobs.length})</span>
          )}
        </h2>
        <div className="flex gap-2">
          <PillBtn onClick={downloadAll} variant="primary">
            Download All
          </PillBtn>
          <PillBtn onClick={cancelAll} variant="muted">
            Cancel All
          </PillBtn>
          <PillBtn onClick={clearDone} variant="danger">
            Clear
          </PillBtn>
        </div>
      </div>

      {/* ---- Queue List ---- */}
      <div className="flex-1 overflow-y-auto px-5 py-2 min-h-0">
        {jobs.length === 0 ? (
          <div className="flex items-center justify-center h-full text-[#48484a] text-sm">
            No downloads yet. Paste a URL and tap Add.
          </div>
        ) : (
          <div className="space-y-2.5">
            {jobs.map((job) => (
              <DownloadCard
                key={job.id}
                job={job}
                onCancel={() => handleCancel(job.id)}
                onRemove={() => removeJob(job.id)}
                onOpen={() => openFolder(job.outputDir).catch(() => {})}
              />
            ))}
          </div>
        )}
      </div>

      {/* ---- Status Bar ---- */}
      <footer className="px-5 py-3 text-xs text-[#48484a] border-t border-[#1c1c1e] flex gap-4 shrink-0">
        {active > 0 && <span>{active} active</span>}
        {queued > 0 && <span>{queued} queued</span>}
        {done > 0 && <span className="text-[#30d158]">{done} done</span>}
        {failed > 0 && <span className="text-[#ff453a]">{failed} failed</span>}
        {jobs.length === 0 && <span>Ready</span>}
      </footer>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function ToggleOption({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label className="flex items-center justify-between cursor-pointer select-none">
      <span className="text-sm">{label}</span>
      <div className="toggle">
        <input
          type="checkbox"
          checked={checked}
          onChange={(e) => onChange(e.target.checked)}
        />
        <div className="toggle-track" />
      </div>
    </label>
  );
}

function PillBtn({
  onClick,
  variant,
  children,
}: {
  onClick: () => void;
  variant: "primary" | "muted" | "danger";
  children: React.ReactNode;
}) {
  const styles = {
    primary: "bg-[#3478f6] hover:bg-[#2c6ade] text-white",
    muted: "bg-[#1c1c1e] hover:bg-[#2c2c2e] text-[#8e8e93]",
    danger: "bg-[#1c1c1e] hover:bg-[#2c2c2e] text-[#ff453a]",
  };
  return (
    <button
      onClick={onClick}
      className={`px-3.5 py-1.5 rounded-full text-xs font-medium transition-colors ${styles[variant]}`}
    >
      {children}
    </button>
  );
}

const STATUS_COLOR: Record<string, string> = {
  queued: "text-[#8e8e93]",
  fetching: "text-[#3478f6]",
  downloading: "text-[#3478f6]",
  processing: "text-[#ff9f0a]",
  finished: "text-[#30d158]",
  error: "text-[#ff453a]",
  cancelled: "text-[#ff9f0a]",
};

const STATUS_LABEL: Record<string, string> = {
  queued: "Queued",
  fetching: "Fetching info...",
  downloading: "Downloading",
  processing: "Processing",
  finished: "Finished",
  error: "Error",
  cancelled: "Cancelled",
};

function DownloadCard({
  job,
  onCancel,
  onRemove,
  onOpen,
}: {
  job: DownloadJob;
  onCancel: () => void;
  onRemove: () => void;
  onOpen: () => void;
}) {
  const isDone = ["finished", "error", "cancelled"].includes(job.status);
  const isActive = ACTIVE_STATUSES.includes(job.status);

  const barColor =
    job.status === "error"
      ? "bg-[#ff453a]"
      : job.status === "finished"
        ? "bg-[#30d158]"
        : job.status === "cancelled"
          ? "bg-[#ff9f0a]"
          : "bg-[#3478f6]";

  return (
    <div className="bg-[#1c1c1e] rounded-[20px] p-4">
      {/* Title + Status */}
      <div className="flex items-start justify-between mb-2">
        <div className="min-w-0 flex-1 mr-4">
          <p className="text-sm font-medium truncate">{job.title}</p>
          <p className="text-xs text-[#48484a] mt-0.5">
            {job.format} &middot; {job.quality}
          </p>
        </div>
        <span
          className={`text-xs font-semibold whitespace-nowrap ${STATUS_COLOR[job.status] ?? "text-[#8e8e93]"}`}
        >
          {STATUS_LABEL[job.status] ?? job.status}
        </span>
      </div>

      {/* Progress bar */}
      <div className="flex items-center gap-3 mb-3">
        <div className="flex-1 bg-[#2c2c2e] rounded-full h-[5px] overflow-hidden">
          <div
            className={`h-full rounded-full transition-all duration-300 ${barColor}`}
            style={{ width: `${Math.min(job.progress, 100)}%` }}
          />
        </div>
        <span className="text-xs text-[#8e8e93] w-10 text-right tabular-nums">
          {Math.round(job.progress)}%
        </span>
      </div>

      {/* Actions + info */}
      <div className="flex items-center justify-between">
        <div className="flex gap-2">
          {!isDone && (
            <SmallPill onClick={onCancel} variant="default">
              Cancel
            </SmallPill>
          )}
          {job.status === "finished" && (
            <SmallPill onClick={onOpen} variant="default">
              Open Folder
            </SmallPill>
          )}
          {isDone && (
            <SmallPill onClick={onRemove} variant="danger">
              Remove
            </SmallPill>
          )}
        </div>
        <div className="text-xs text-[#48484a] truncate max-w-[50%] text-right">
          {isActive && job.speed && <span>{job.speed}</span>}
          {isActive && job.eta && <span className="ml-2">ETA {job.eta}</span>}
          {job.status === "error" && job.error && (
            <span className="text-[#ff453a]" title={job.error}>
              {job.error.length > 50 ? job.error.slice(0, 50) + "..." : job.error}
            </span>
          )}
          {job.status === "finished" && <span className="text-[#30d158]">Complete</span>}
        </div>
      </div>
    </div>
  );
}

function SmallPill({
  onClick,
  variant,
  children,
}: {
  onClick: () => void;
  variant: "default" | "danger";
  children: React.ReactNode;
}) {
  const cls =
    variant === "danger"
      ? "bg-[#2c2c2e] hover:bg-[#3a3a3c] text-[#ff453a]"
      : "bg-[#2c2c2e] hover:bg-[#3a3a3c] text-[#8e8e93]";
  return (
    <button
      onClick={onClick}
      className={`px-3.5 py-1 text-xs rounded-full transition-colors ${cls}`}
    >
      {children}
    </button>
  );
}

export default App;
