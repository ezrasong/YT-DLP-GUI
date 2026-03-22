using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YtDlpGui;

public class YtDlpService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private static readonly bool IsWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private string? _ytDlpPath;
    private string? _ffmpegPath;

    public string? YtDlpPath => _ytDlpPath ??= FindBinary(IsWin ? "yt-dlp.exe" : "yt-dlp");
    public string? FfmpegPath => _ffmpegPath ??= FindBinary(IsWin ? "ffmpeg.exe" : "ffmpeg");

    public bool IsInstalled => YtDlpPath != null && File.Exists(YtDlpPath);
    public bool IsFfmpegAvailable => FfmpegPath != null && File.Exists(FfmpegPath);

    private static string AppDataDir
    {
        get
        {
            string dir;
            if (IsWin)
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yt-dlp-gui");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/yt-dlp-gui");
            else
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/yt-dlp-gui");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string? FindBinary(string name)
    {
        // 1. Next to the exe (bundled via PublishSingleFile content)
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside)) return beside;

        // 2. In bin/ subfolder (dev mode)
        var bin = Path.Combine(AppContext.BaseDirectory, "bin", name);
        if (File.Exists(bin)) return bin;

        // 3. App data dir (downloaded at runtime)
        var appData = Path.Combine(AppDataDir, name);
        if (File.Exists(appData)) return appData;

        return null;
    }

    public async Task<string> GetVersionAsync()
    {
        if (!IsInstalled) return "";
        try
        {
            var psi = new ProcessStartInfo(YtDlpPath!) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--version");
            var p = Process.Start(psi)!;
            var output = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    public async Task<string> GetLatestVersionAsync()
    {
        try
        {
            Http.DefaultRequestHeaders.UserAgent.TryParseAdd("yt-dlp-gui");
            var json = await Http.GetStringAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        }
        catch { return ""; }
    }

    public async Task<(YtDlpStatus Status, string CurrentVersion, string LatestVersion, string Error)> CheckStatusAsync()
    {
        if (!IsInstalled)
            return (YtDlpStatus.NotInstalled, "", "", "");
        try
        {
            var current = await GetVersionAsync();
            var latest = await GetLatestVersionAsync();
            if (string.IsNullOrEmpty(latest))
                return (YtDlpStatus.UpToDate, current, "", "");
            return current != latest
                ? (YtDlpStatus.UpdateAvailable, current, latest, "")
                : (YtDlpStatus.UpToDate, current, latest, "");
        }
        catch (Exception ex) { return (YtDlpStatus.Error, "", "", ex.Message); }
    }

    public async Task<string> InstallOrUpdateAsync(Action<string>? onProgress = null)
    {
        onProgress?.Invoke("Fetching release info...");
        var json = await Http.GetStringAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "unknown";
        var assets = root.GetProperty("assets");

        var assetName = IsWin ? "yt-dlp.exe"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "yt-dlp_macos"
            : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "yt-dlp_linux_aarch64"
            : "yt-dlp_linux";

        string? downloadUrl = null;
        foreach (var asset in assets.EnumerateArray())
            if (asset.GetProperty("name").GetString() == assetName)
            { downloadUrl = asset.GetProperty("browser_download_url").GetString(); break; }

        if (downloadUrl == null) throw new Exception("No compatible binary found");

        var dest = Path.Combine(AppDataDir, IsWin ? "yt-dlp.exe" : "yt-dlp");
        onProgress?.Invoke($"Downloading yt-dlp {tag}...");
        var bytes = await Http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(dest, bytes);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _ytDlpPath = dest;
        return tag;
    }

    public List<string> BuildCommand(string url, FormatOption format, QualityOption quality,
        string outputDir, bool subtitles, bool sponsorBlock, bool fullPlaylist)
    {
        var cmd = new List<string> { YtDlpPath!, "--newline", "--no-colors" };

        if (format.IsVideo)
        {
            cmd.AddRange(new[] { "-f", quality.Arg, "--merge-output-format", format.Extension });
        }
        else
        {
            cmd.AddRange(new[] { "-x", "--audio-format", format.Extension });
            if (quality.Arg != "0") cmd.AddRange(new[] { "--audio-quality", quality.Arg });
        }

        if (subtitles) cmd.AddRange(new[] { "--write-subs", "--sub-langs", "all" });
        if (sponsorBlock) cmd.AddRange(new[] { "--sponsorblock-remove", "all" });
        if (!fullPlaylist) cmd.Add("--no-playlist");

        cmd.AddRange(new[] { "-o", Path.Combine(outputDir, "%(title)s.%(ext)s") });

        if (FfmpegPath != null) cmd.AddRange(new[] { "--ffmpeg-location", Path.GetDirectoryName(FfmpegPath)! });

        cmd.Add(url);
        return cmd;
    }

    public record ProgressUpdate(string? Title = null, float? Progress = null, string? Speed = null,
        string? Eta = null, DownloadState? State = null, string? Error = null);

    public async Task RunDownloadAsync(string url, FormatOption format, QualityOption quality,
        string outputDir, bool subtitles, bool sponsorBlock, bool fullPlaylist,
        Action<ProgressUpdate> onUpdate, CancellationToken ct)
    {
        var args = BuildCommand(url, format, quality, outputDir, subtitles, sponsorBlock, fullPlaylist);
        var psi = new ProcessStartInfo(args[0])
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in args.Skip(1)) psi.ArgumentList.Add(a);

        var process = Process.Start(psi)!;
        ct.Register(() => { try { process.Kill(true); } catch { } });

        onUpdate(new(State: DownloadState.Fetching));

        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            ParseLine(line, onUpdate);

        await process.WaitForExitAsync(ct);
        if (ct.IsCancellationRequested) return;

        if (process.ExitCode == 0)
            onUpdate(new(State: DownloadState.Finished, Progress: 1f));
        else
            onUpdate(new(State: DownloadState.Error, Error: $"Exit code {process.ExitCode}"));
    }

    private static void ParseLine(string line, Action<ProgressUpdate> onUpdate)
    {
        if (line.Contains("[download]") && line.Contains('%'))
        {
            var progress = Regex.Match(line, @"(\d+\.?\d*)%");
            var speed = Regex.Match(line, @"at\s+(\S+/s)");
            var eta = Regex.Match(line, @"ETA\s+(\S+)");
            onUpdate(new(State: DownloadState.Downloading,
                Progress: progress.Success ? float.Parse(progress.Groups[1].Value) / 100f : null,
                Speed: speed.Success ? speed.Groups[1].Value : "",
                Eta: eta.Success ? eta.Groups[1].Value : ""));
        }
        else if (line.Contains("[download] Destination:"))
        {
            var path = line.Split("Destination:")[1].Trim();
            onUpdate(new(Title: Path.GetFileNameWithoutExtension(path)));
        }
        else if (line.Contains("[ExtractAudio]") || line.Contains("[Merger]") || line.Contains("[SponsorBlock]"))
            onUpdate(new(State: DownloadState.Processing));
        else if (line.Contains("ERROR:"))
            onUpdate(new(State: DownloadState.Error, Error: line.Split("ERROR:")[1].Trim()));
    }
}
