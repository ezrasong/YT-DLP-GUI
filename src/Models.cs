using System;
using System.IO;

namespace YtDlpGui;

public enum DownloadState { Queued, Fetching, Downloading, Processing, Finished, Error, Cancelled }

public class FormatOption
{
    public string Label { get; init; } = "";
    public bool IsVideo { get; init; }
    public string Extension { get; init; } = "";
    public override string ToString() => Label;

    public static readonly FormatOption[] All =
    [
        new() { Label = "Video (MP4)", IsVideo = true, Extension = "mp4" },
        new() { Label = "Video (MKV)", IsVideo = true, Extension = "mkv" },
        new() { Label = "Video (WEBM)", IsVideo = true, Extension = "webm" },
        new() { Label = "Audio (MP3)", IsVideo = false, Extension = "mp3" },
        new() { Label = "Audio (M4A)", IsVideo = false, Extension = "m4a" },
        new() { Label = "Audio (FLAC)", IsVideo = false, Extension = "flac" },
        new() { Label = "Audio (WAV)", IsVideo = false, Extension = "wav" },
        new() { Label = "Audio (OPUS)", IsVideo = false, Extension = "opus" },
    ];
}

public class QualityOption
{
    public string Label { get; init; } = "";
    public string Arg { get; init; } = "";
    public override string ToString() => Label;

    public static readonly QualityOption[] Video =
    [
        new() { Label = "Best", Arg = "bestvideo+bestaudio/best" },
        new() { Label = "4K (2160p)", Arg = "bestvideo[height<=2160]+bestaudio/best[height<=2160]" },
        new() { Label = "1440p", Arg = "bestvideo[height<=1440]+bestaudio/best[height<=1440]" },
        new() { Label = "1080p", Arg = "bestvideo[height<=1080]+bestaudio/best[height<=1080]" },
        new() { Label = "720p", Arg = "bestvideo[height<=720]+bestaudio/best[height<=720]" },
        new() { Label = "480p", Arg = "bestvideo[height<=480]+bestaudio/best[height<=480]" },
        new() { Label = "360p", Arg = "bestvideo[height<=360]+bestaudio/best[height<=360]" },
    ];

    public static readonly QualityOption[] Audio =
    [
        new() { Label = "Best", Arg = "0" },
        new() { Label = "320 kbps", Arg = "320" },
        new() { Label = "256 kbps", Arg = "256" },
        new() { Label = "192 kbps", Arg = "192" },
        new() { Label = "128 kbps", Arg = "128" },
    ];
}

public enum YtDlpStatus { NotInstalled, Checking, UpToDate, UpdateAvailable, Installing, Updating, Error }

public static class Defaults
{
    public static string DownloadDir
    {
        get
        {
            var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(dl) ? dl : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }
}
