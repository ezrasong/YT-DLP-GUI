using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using YtDlpGui.Wpf.Models;

namespace YtDlpGui.Wpf.Services;

public sealed record UpdateStatus(
    bool Installed,
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    string Error);

public sealed record DownloadProgress(
    double Percent,
    string Speed,
    string Eta,
    string Status,
    string Title,
    string Error);

public sealed class YtDlpService
{
    private const string YtDlpWindowsUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string YtDlpLatestApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

    private static readonly Regex PercentRegex = new(@"\[download\]\s+([\d.]+)%", RegexOptions.Compiled);
    private static readonly Regex DetailRegex = new(@"at\s+(.+?)\s+ETA\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex DestinationRegex = new(@"\[download\] Destination:\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex AlreadyRegex = new(@"\[download\]\s+(.+) has already been downloaded", RegexOptions.Compiled);
    private static readonly Regex ProcessingRegex = new(@"^\[(ffmpeg|ExtractAudio|Merger|Merge|SponsorBlock|ModifyChapters)\]", RegexOptions.Compiled);
    private static readonly Regex ErrorRegex = new(@"^ERROR:\s+(.+)$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _binDirectory;
    private readonly string _ytdlpPath;
    private readonly string _ffmpegPath;

    public YtDlpService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("YtDlpGuiNative", "1.0"));

        _binDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YtDlpGuiNative",
            "bin");
        _ytdlpPath = Path.Combine(_binDirectory, "yt-dlp.exe");
        _ffmpegPath = Path.Combine(_binDirectory, "ffmpeg.exe");
    }

    public string GetDefaultDownloadDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(profile, "Downloads");
        return Directory.Exists(downloads) ? downloads : profile;
    }

    public async Task<string> GetInstalledVersionAsync(CancellationToken cancellationToken = default)
    {
        return await GetVersionFromPathAsync(_ytdlpPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpdateStatus> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
        var installed = !string.IsNullOrWhiteSpace(currentVersion);

        try
        {
            var latestVersion = await GetLatestVersionAsync(cancellationToken).ConfigureAwait(false);
            var updateAvailable = installed && IsNewerVersion(currentVersion, latestVersion);
            return new UpdateStatus(installed, currentVersion, latestVersion, updateAvailable, string.Empty);
        }
        catch (Exception ex)
        {
            return new UpdateStatus(installed, currentVersion, string.Empty, false, ex.Message);
        }
    }

    public async Task<string> InstallOrUpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureYtDlpInstalledAsync(cancellationToken, forceDownload: true).ConfigureAwait(false);
        var version = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("yt-dlp install finished but version check failed.");
        }
        return version;
    }

    public async Task RunDownloadAsync(
        DownloadJob job,
        Action<DownloadProgress> onProgress,
        CancellationToken cancellationToken = default)
    {
        await EnsureYtDlpInstalledAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(job.OutputDirectory);

        var args = BuildArgs(job);
        var startInfo = new ProcessStartInfo
        {
            FileName = _ytdlpPath,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = job.OutputDirectory,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start yt-dlp process.");
        }

        using var reg = cancellationToken.Register(() => TryKill(process));
        onProgress(new DownloadProgress(0, string.Empty, string.Empty, "fetching", string.Empty, string.Empty));

        string title = string.Empty;
        string lastError = string.Empty;
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var errorMatch = ErrorRegex.Match(line);
            if (errorMatch.Success)
            {
                lastError = errorMatch.Groups[1].Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = ExtractTitle(line);
            }

            var pctMatch = PercentRegex.Match(line);
            if (pctMatch.Success)
            {
                var percentText = pctMatch.Groups[1].Value;
                if (!double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    percent = 0;
                }

                var (speed, eta) = ExtractSpeedEta(line);
                onProgress(new DownloadProgress(percent, speed, eta, "downloading", title, string.Empty));
                continue;
            }

            if (ProcessingRegex.IsMatch(line))
            {
                onProgress(new DownloadProgress(100, string.Empty, string.Empty, "processing", title, string.Empty));
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            onProgress(new DownloadProgress(0, string.Empty, string.Empty, "cancelled", title, string.Empty));
            return;
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode == 0)
        {
            onProgress(new DownloadProgress(100, string.Empty, string.Empty, "finished", title, string.Empty));
            return;
        }

        var message = string.IsNullOrWhiteSpace(lastError)
            ? $"yt-dlp exited with code {process.ExitCode}"
            : lastError;
        onProgress(new DownloadProgress(0, string.Empty, string.Empty, "error", title, message));
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            Arguments = $"\"{path}\"",
        };
        Process.Start(startInfo);
    }

    private async Task EnsureYtDlpInstalledAsync(CancellationToken cancellationToken, bool forceDownload = false)
    {
        Directory.CreateDirectory(_binDirectory);
        var current = await GetInstalledVersionAsync(cancellationToken).ConfigureAwait(false);
        if (!forceDownload && !string.IsNullOrWhiteSpace(current))
        {
            return;
        }
        await DownloadYtDlpAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadYtDlpAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_binDirectory);
        var tempPath = _ytdlpPath + ".tmp";

        using var response = await _http.GetAsync(
            YtDlpWindowsUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var output = File.Create(tempPath))
        {
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_ytdlpPath))
        {
            File.Delete(_ytdlpPath);
        }
        File.Move(tempPath, _ytdlpPath);
    }

    private async Task<string> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, YtDlpLatestApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("tag_name", out var tag))
        {
            throw new InvalidOperationException("Latest release response did not include tag_name.");
        }

        return tag.GetString()?.Trim().TrimStart('v') ?? string.Empty;
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest))
        {
            return false;
        }

        static List<int>? Parse(string input)
        {
            var parts = new List<int>();
            foreach (var piece in input.Split('.'))
            {
                if (!int.TryParse(piece, out var parsed))
                {
                    return null;
                }
                parts.Add(parsed);
            }
            return parts;
        }

        var c = Parse(current);
        var l = Parse(latest);
        if (c is null || l is null)
        {
            return !string.Equals(current, latest, StringComparison.OrdinalIgnoreCase);
        }

        var max = Math.Max(c.Count, l.Count);
        for (var i = 0; i < max; i += 1)
        {
            var cv = i < c.Count ? c[i] : 0;
            var lv = i < l.Count ? l[i] : 0;
            if (lv > cv)
            {
                return true;
            }
            if (lv < cv)
            {
                return false;
            }
        }
        return false;
    }

    private static string ExtractTitle(string line)
    {
        var destination = DestinationRegex.Match(line);
        if (!destination.Success)
        {
            destination = AlreadyRegex.Match(line);
        }
        if (!destination.Success)
        {
            return string.Empty;
        }

        var target = destination.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var stem = Path.GetFileNameWithoutExtension(target);
        return stem ?? string.Empty;
    }

    private static (string Speed, string Eta) ExtractSpeedEta(string line)
    {
        var detail = DetailRegex.Match(line);
        if (!detail.Success)
        {
            return (string.Empty, string.Empty);
        }

        var speed = detail.Groups[1].Value.Trim();
        var eta = detail.Groups[2].Value.Trim();
        if (speed.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            speed = string.Empty;
        }
        if (eta.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            eta = string.Empty;
        }
        return (speed, eta);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort kill.
        }
    }

    private async Task<string> GetVersionFromPathAsync(string binaryPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(binaryPath))
        {
            return string.Empty;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--version");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return string.Empty;
        }

        using var reg = cancellationToken.Register(() => TryKill(process));
        var version = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return string.Empty;
        }

        return version?.Trim() ?? string.Empty;
    }

    private IReadOnlyList<string> BuildArgs(DownloadJob job)
    {
        var args = new List<string>
        {
            "--newline",
            "-o",
            Path.Combine(job.OutputDirectory, "%(title)s.%(ext)s"),
        };

        if (job.Format.StartsWith("Video", StringComparison.OrdinalIgnoreCase))
        {
            var ext = ParseParenthetical(job.Format, "MP4").ToLowerInvariant();
            var formatSelector = job.Quality switch
            {
                "Best" => "bestvideo+bestaudio/best",
                "4K" => "bestvideo[height<=2160]+bestaudio/best[height<=2160]/best",
                "1440p" => "bestvideo[height<=1440]+bestaudio/best[height<=1440]/best",
                "1080p" => "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best",
                "720p" => "bestvideo[height<=720]+bestaudio/best[height<=720]/best",
                "480p" => "bestvideo[height<=480]+bestaudio/best[height<=480]/best",
                "360p" => "bestvideo[height<=360]+bestaudio/best[height<=360]/best",
                _ => "bestvideo+bestaudio/best",
            };
            args.Add("-f");
            args.Add(formatSelector);
            if (!string.Equals(ext, "webm", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--merge-output-format");
                args.Add(ext);
            }
        }
        else
        {
            var codec = ParseParenthetical(job.Format, "MP3").ToLowerInvariant();
            args.Add("-f");
            args.Add("bestaudio/best");
            args.Add("-x");
            args.Add("--audio-format");
            args.Add(codec);

            var bitrate = job.Quality switch
            {
                "320kbps" => "320",
                "256kbps" => "256",
                "192kbps" => "192",
                "128kbps" => "128",
                _ => string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(bitrate))
            {
                args.Add("--audio-quality");
                args.Add(bitrate);
            }
        }

        if (!job.Playlist)
        {
            args.Add("--no-playlist");
        }
        if (job.Subtitles)
        {
            args.Add("--write-subs");
            args.Add("--write-auto-subs");
            args.Add("--sub-langs");
            args.Add("en,en.*");
        }
        if (job.SponsorBlock)
        {
            args.Add("--sponsorblock-remove");
            args.Add("sponsor,selfpromo,interaction");
        }
        if (File.Exists(_ffmpegPath))
        {
            args.Add("--ffmpeg-location");
            args.Add(_binDirectory);
        }

        args.Add(job.Url);
        return args;
    }

    private static string ParseParenthetical(string input, string fallback)
    {
        var open = input.IndexOf('(');
        var close = input.IndexOf(')');
        if (open < 0 || close <= open)
        {
            return fallback;
        }
        return input[(open + 1)..close].Trim();
    }
}



