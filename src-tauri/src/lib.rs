use regex::Regex;
use serde::Serialize;
use std::collections::HashMap;
use std::io::{BufRead, BufReader};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::{Arc, Mutex};
use tauri::{AppHandle, Emitter, Manager, State};

const TARGET_TRIPLE: &str = env!("TARGET_TRIPLE");

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

struct DownloadHandle {
    cancel: Arc<AtomicBool>,
    pid: Arc<AtomicU32>,
}

struct AppState {
    downloads: Arc<Mutex<HashMap<String, DownloadHandle>>>,
    bin_dir: PathBuf,
}

// ---------------------------------------------------------------------------
// Payloads
// ---------------------------------------------------------------------------

#[derive(Serialize, Clone)]
struct ProgressPayload {
    id: String,
    percent: f64,
    speed: String,
    eta: String,
    status: String,
    title: String,
    error: String,
}

#[derive(Serialize, Clone)]
struct InitResult {
    ytdlp: bool,
    ffmpeg: bool,
    ytdlp_version: String,
    download_dir: String,
}

// ---------------------------------------------------------------------------
// Binary resolution
// ---------------------------------------------------------------------------

/// Find a binary: first check the writable app-data bin dir, then the sidecar
/// location next to the executable, then fall back to bare name (system PATH).
fn resolve_bin(bin_dir: &Path, name: &str) -> PathBuf {
    let ext = if cfg!(windows) { ".exe" } else { "" };

    // 1. Writable copy in app data
    let local = bin_dir.join(format!("{name}{ext}"));
    if local.exists() {
        return local;
    }

    // 2. Sidecar next to the running executable
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let sidecar = dir.join(format!("{name}-{TARGET_TRIPLE}{ext}"));
            if sidecar.exists() {
                return sidecar;
            }
        }
    }

    // 3. System PATH
    PathBuf::from(format!("{name}{ext}"))
}

/// Copy a sidecar binary into the writable bin dir with a plain name so that
/// yt-dlp can discover ffmpeg by its expected filename.
fn ensure_local_copy(bin_dir: &Path, name: &str) {
    let ext = if cfg!(windows) { ".exe" } else { "" };
    let dest = bin_dir.join(format!("{name}{ext}"));
    if dest.exists() {
        return;
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let sidecar = dir.join(format!("{name}-{TARGET_TRIPLE}{ext}"));
            if sidecar.exists() {
                let _ = std::fs::copy(&sidecar, &dest);
                #[cfg(unix)]
                {
                    use std::os::unix::fs::PermissionsExt;
                    let _ =
                        std::fs::set_permissions(&dest, std::fs::Permissions::from_mode(0o755));
                }
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

#[tauri::command]
fn init_app(state: State<'_, AppState>) -> InitResult {
    let bin = &state.bin_dir;

    // Copy sidecars to writable location so yt-dlp --update and ffmpeg
    // discovery work correctly.
    ensure_local_copy(bin, "yt-dlp");
    ensure_local_copy(bin, "ffmpeg");

    let ytdlp_path = resolve_bin(bin, "yt-dlp");
    let (ytdlp, version) = match Command::new(&ytdlp_path).arg("--version").output() {
        Ok(out) if out.status.success() => {
            (true, String::from_utf8_lossy(&out.stdout).trim().to_string())
        }
        _ => (false, String::new()),
    };

    let ffmpeg_path = resolve_bin(bin, "ffmpeg");
    let ffmpeg = Command::new(&ffmpeg_path)
        .arg("-version")
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .map(|s| s.success())
        .unwrap_or(false);

    let download_dir = dirs::download_dir()
        .or_else(|| dirs::home_dir().map(|h| h.join("Downloads")))
        .map(|p| p.to_string_lossy().to_string())
        .unwrap_or_default();

    InitResult {
        ytdlp,
        ffmpeg,
        ytdlp_version: version,
        download_dir,
    }
}

#[tauri::command]
fn update_ytdlp(state: State<'_, AppState>) -> Result<String, String> {
    let ext = if cfg!(windows) { ".exe" } else { "" };
    let dest = state.bin_dir.join(format!("yt-dlp{ext}"));

    let url = if cfg!(target_os = "macos") {
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos"
    } else if cfg!(target_os = "windows") {
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
    } else {
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp"
    };

    let status = Command::new("curl")
        .args(["-L", "-o", &dest.to_string_lossy(), url])
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .map_err(|e| format!("curl failed: {e}"))?;

    if !status.success() {
        return Err("Download failed".into());
    }

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&dest, std::fs::Permissions::from_mode(0o755))
            .map_err(|e| e.to_string())?;
    }

    let out = Command::new(&dest)
        .arg("--version")
        .output()
        .map_err(|e| e.to_string())?;
    Ok(String::from_utf8_lossy(&out.stdout).trim().to_string())
}

#[tauri::command]
fn open_folder(path: String) -> Result<(), String> {
    let r = if cfg!(target_os = "macos") {
        Command::new("open").arg(&path).spawn()
    } else if cfg!(target_os = "windows") {
        Command::new("explorer").arg(&path).spawn()
    } else {
        Command::new("xdg-open").arg(&path).spawn()
    };
    r.map(|_| ()).map_err(|e| e.to_string())
}

#[tauri::command]
fn cancel_download(state: State<'_, AppState>, id: String) -> Result<(), String> {
    let (cancel, pid_arc) = {
        let map = state.downloads.lock().map_err(|e| e.to_string())?;
        match map.get(&id) {
            Some(h) => (h.cancel.clone(), h.pid.clone()),
            None => return Ok(()),
        }
    };
    cancel.store(true, Ordering::Relaxed);
    let pid = pid_arc.load(Ordering::Relaxed);
    if pid != 0 {
        kill_pid(pid);
    }
    Ok(())
}

#[tauri::command]
fn start_download(
    app: AppHandle,
    state: State<'_, AppState>,
    id: String,
    url: String,
    format: String,
    quality: String,
    output_dir: String,
    subtitles: bool,
    sponsor_block: bool,
    playlist: bool,
) -> Result<(), String> {
    let cancel = Arc::new(AtomicBool::new(false));
    let pid = Arc::new(AtomicU32::new(0));

    state.downloads.lock().map_err(|e| e.to_string())?.insert(
        id.clone(),
        DownloadHandle {
            cancel: cancel.clone(),
            pid: pid.clone(),
        },
    );

    let downloads = state.downloads.clone();
    let bin_dir = state.bin_dir.clone();

    std::thread::spawn(move || {
        do_download(
            app,
            downloads,
            bin_dir,
            id,
            url,
            format,
            quality,
            output_dir,
            subtitles,
            sponsor_block,
            playlist,
            cancel,
            pid,
        );
    });

    Ok(())
}

// ---------------------------------------------------------------------------
// Download logic
// ---------------------------------------------------------------------------

#[allow(clippy::too_many_arguments)]
fn do_download(
    app: AppHandle,
    downloads: Arc<Mutex<HashMap<String, DownloadHandle>>>,
    bin_dir: PathBuf,
    id: String,
    url: String,
    format: String,
    quality: String,
    output_dir: String,
    subtitles: bool,
    sponsor_block: bool,
    playlist: bool,
    cancel: Arc<AtomicBool>,
    pid_store: Arc<AtomicU32>,
) {
    let emit = |status: &str, pct: f64, speed: &str, eta: &str, title: &str, error: &str| {
        let _ = app.emit(
            "download-progress",
            ProgressPayload {
                id: id.clone(),
                percent: pct,
                speed: speed.into(),
                eta: eta.into(),
                status: status.into(),
                title: title.into(),
                error: error.into(),
            },
        );
    };

    emit("fetching", 0.0, "", "", "", "");

    let ytdlp = resolve_bin(&bin_dir, "yt-dlp");
    let ffmpeg_dir = bin_dir.to_string_lossy().to_string();

    let mut args = build_args(
        &url,
        &format,
        &quality,
        &output_dir,
        subtitles,
        sponsor_block,
        playlist,
    );
    // Point yt-dlp to our bundled ffmpeg
    args.push("--ffmpeg-location".into());
    args.push(ffmpeg_dir);

    let mut child = match Command::new(&ytdlp)
        .args(&args)
        .stdout(Stdio::null())
        .stderr(Stdio::piped())
        .spawn()
    {
        Ok(c) => c,
        Err(e) => {
            emit(
                "error",
                0.0,
                "",
                "",
                "",
                &format!("Failed to start yt-dlp: {e}"),
            );
            cleanup(&downloads, &id);
            return;
        }
    };

    pid_store.store(child.id(), Ordering::Relaxed);

    if cancel.load(Ordering::Relaxed) {
        let _ = child.kill();
        let _ = child.wait();
        emit("cancelled", 0.0, "", "", "", "");
        cleanup(&downloads, &id);
        return;
    }

    let stderr = match child.stderr.take() {
        Some(s) => s,
        None => {
            emit("error", 0.0, "", "", "", "Could not capture yt-dlp output");
            let _ = child.wait();
            cleanup(&downloads, &id);
            return;
        }
    };

    let pct_re = Regex::new(r"\[download\]\s+([\d.]+)%").unwrap();
    let detail_re = Regex::new(r"at\s+(.+?)\s+ETA\s+(.+)").unwrap();
    let dest_re = Regex::new(r"\[download\] Destination:\s+(.+)").unwrap();
    let already_re = Regex::new(r"\[download\]\s+(.+) has already been downloaded").unwrap();
    let proc_re =
        Regex::new(r"^\[(ffmpeg|ExtractAudio|Merger|Merge|SponsorBlock|ModifyChapters)\]").unwrap();

    let mut title = String::new();

    for line in BufReader::new(stderr).lines() {
        if cancel.load(Ordering::Relaxed) {
            break;
        }
        let line = match line {
            Ok(l) => l,
            Err(_) => break,
        };

        if title.is_empty() {
            if let Some(caps) = dest_re.captures(&line).or_else(|| already_re.captures(&line)) {
                if let Some(m) = caps.get(1) {
                    if let Some(stem) = Path::new(m.as_str()).file_stem() {
                        title = stem.to_string_lossy().to_string();
                    }
                }
            }
        }

        if let Some(caps) = pct_re.captures(&line) {
            let pct: f64 = caps
                .get(1)
                .map_or(0.0, |m| m.as_str().parse().unwrap_or(0.0));
            let (speed, eta) = detail_re
                .captures(&line)
                .map(|dc| {
                    let s = dc.get(1).map_or("", |m| m.as_str()).trim();
                    let e = dc.get(2).map_or("", |m| m.as_str()).trim();
                    (
                        if s.contains("Unknown") {
                            ""
                        } else {
                            s
                        }
                        .to_string(),
                        if e.contains("Unknown") {
                            ""
                        } else {
                            e
                        }
                        .to_string(),
                    )
                })
                .unwrap_or_default();
            emit("downloading", pct, &speed, &eta, &title, "");
            continue;
        }

        if proc_re.is_match(&line) {
            emit("processing", 100.0, "", "", &title, "");
        }
    }

    if cancel.load(Ordering::Relaxed) {
        let _ = child.kill();
    }

    match child.wait() {
        Ok(s) if s.success() => emit("finished", 100.0, "", "", &title, ""),
        Ok(_) if cancel.load(Ordering::Relaxed) => emit("cancelled", 0.0, "", "", &title, ""),
        Ok(s) => emit(
            "error",
            0.0,
            "",
            "",
            &title,
            &format!("yt-dlp exited with code {}", s.code().unwrap_or(-1)),
        ),
        Err(e) if cancel.load(Ordering::Relaxed) => {
            let _ = e;
            emit("cancelled", 0.0, "", "", &title, "");
        }
        Err(e) => emit("error", 0.0, "", "", &title, &format!("Process error: {e}")),
    }

    cleanup(&downloads, &id);
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn build_args(
    url: &str,
    format: &str,
    quality: &str,
    output_dir: &str,
    subtitles: bool,
    sponsor_block: bool,
    playlist: bool,
) -> Vec<String> {
    let mut a = vec![
        "--newline".into(),
        "-o".into(),
        format!("{output_dir}/%(title)s.%(ext)s"),
    ];

    if format.starts_with("Video") {
        let ext = format.split('(').nth(1).unwrap_or("MP4)").trim_end_matches(')');
        let f = match quality {
            "Best" => "bestvideo+bestaudio/best".into(),
            "Worst" => "worstvideo+worstaudio/worst".into(),
            q => {
                let h = match q {
                    "4K" => 2160,
                    "1440p" => 1440,
                    "1080p" => 1080,
                    "720p" => 720,
                    "480p" => 480,
                    "360p" => 360,
                    _ => 1080,
                };
                format!("bestvideo[height<={h}]+bestaudio/best[height<={h}]/best")
            }
        };
        a.extend(["-f".into(), f]);
        if ext.to_uppercase() != "WEBM" {
            a.extend(["--merge-output-format".into(), ext.to_lowercase()]);
        }
    } else {
        let codec = format
            .split('(')
            .nth(1)
            .unwrap_or("MP3)")
            .trim_end_matches(')')
            .to_lowercase();
        a.extend([
            "-f".into(),
            "bestaudio/best".into(),
            "-x".into(),
            "--audio-format".into(),
            codec,
        ]);
        if let Some(b) = match quality {
            "320kbps" => Some("320"),
            "256kbps" => Some("256"),
            "192kbps" => Some("192"),
            "128kbps" => Some("128"),
            _ => None,
        } {
            a.extend(["--audio-quality".into(), b.into()]);
        }
    }

    if !playlist {
        a.push("--no-playlist".into());
    }
    if subtitles {
        a.extend([
            "--write-subs".into(),
            "--write-auto-subs".into(),
            "--sub-langs".into(),
            "en,en.*".into(),
        ]);
    }
    if sponsor_block {
        a.extend([
            "--sponsorblock-remove".into(),
            "sponsor,selfpromo,interaction".into(),
        ]);
    }

    a.push(url.into());
    a
}

fn cleanup(downloads: &Arc<Mutex<HashMap<String, DownloadHandle>>>, id: &str) {
    if let Ok(mut m) = downloads.lock() {
        m.remove(id);
    }
}

fn kill_pid(pid: u32) {
    if cfg!(target_os = "windows") {
        let _ = Command::new("taskkill")
            .args(["/PID", &pid.to_string(), "/F"])
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status();
    } else {
        let _ = Command::new("kill")
            .arg(pid.to_string())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status();
    }
}

// ---------------------------------------------------------------------------
// App entry
// ---------------------------------------------------------------------------

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let data_dir = app.path().app_data_dir()?;
            let bin_dir = data_dir.join("bin");
            std::fs::create_dir_all(&bin_dir)?;
            app.manage(AppState {
                downloads: Arc::new(Mutex::new(HashMap::new())),
                bin_dir,
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            init_app,
            update_ytdlp,
            open_folder,
            cancel_download,
            start_download,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
