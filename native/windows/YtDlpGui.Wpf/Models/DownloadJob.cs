using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace YtDlpGui.Wpf.Models;

public enum DownloadStatus
{
    Queued,
    Fetching,
    Downloading,
    Processing,
    Finished,
    Error,
    Cancelled,
}

public sealed class DownloadJob : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private DownloadStatus _status = DownloadStatus.Queued;
    private double _progress;
    private string _speed = string.Empty;
    private string _eta = string.Empty;
    private string _error = string.Empty;

    public DownloadJob(
        string id,
        string url,
        string format,
        string quality,
        string outputDirectory,
        bool subtitles,
        bool sponsorBlock,
        bool playlist)
    {
        Id = id;
        Url = url;
        Title = url;
        Format = format;
        Quality = quality;
        OutputDirectory = outputDirectory;
        Subtitles = subtitles;
        SponsorBlock = sponsorBlock;
        Playlist = playlist;
    }

    public string Id { get; }
    public string Url { get; }
    public string Format { get; }
    public string Quality { get; }
    public string OutputDirectory { get; }
    public bool Subtitles { get; }
    public bool SponsorBlock { get; }
    public bool Playlist { get; }
    public CancellationTokenSource? Cancellation { get; set; }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(IsTerminal));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(DetailsLabel));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set => Set(ref _progress, value);
    }

    public string Speed
    {
        get => _speed;
        set
        {
            if (Set(ref _speed, value))
            {
                OnPropertyChanged(nameof(DetailsLabel));
            }
        }
    }

    public string Eta
    {
        get => _eta;
        set
        {
            if (Set(ref _eta, value))
            {
                OnPropertyChanged(nameof(DetailsLabel));
            }
        }
    }

    public string Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                OnPropertyChanged(nameof(DetailsLabel));
            }
        }
    }

    public string StatusLabel =>
        Status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.Fetching => "Fetching info...",
            DownloadStatus.Downloading => "Downloading",
            DownloadStatus.Processing => "Processing",
            DownloadStatus.Finished => "Finished",
            DownloadStatus.Error => "Error",
            DownloadStatus.Cancelled => "Cancelled",
            _ => Status.ToString(),
        };

    public bool IsTerminal =>
        Status is DownloadStatus.Finished or DownloadStatus.Error or DownloadStatus.Cancelled;

    public bool IsActive => Status is DownloadStatus.Fetching or DownloadStatus.Downloading or DownloadStatus.Processing;

    public string DetailsLabel
    {
        get
        {
            if (Status == DownloadStatus.Error && !string.IsNullOrWhiteSpace(Error))
            {
                return Error;
            }

            if (IsActive)
            {
                var bits = new List<string>();
                if (!string.IsNullOrWhiteSpace(Speed))
                {
                    bits.Add(Speed);
                }
                if (!string.IsNullOrWhiteSpace(Eta))
                {
                    bits.Add($"ETA {Eta}");
                }
                return string.Join(" | ", bits);
            }

            if (Status == DownloadStatus.Finished)
            {
                return "Complete";
            }

            return $"{Format} | {Quality}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

