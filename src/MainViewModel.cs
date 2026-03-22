using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace YtDlpGui;

public partial class MainViewModel : ObservableObject
{
    private readonly YtDlpService _svc = new();
    private int _activeDownloads;

    // URL input
    [ObservableProperty] private string _url = "";

    // Options
    [ObservableProperty] private FormatOption _selectedFormat = FormatOption.All[0];
    [ObservableProperty] private QualityOption _selectedQuality = QualityOption.Video[0];
    [ObservableProperty] private string _outputDir = Defaults.DownloadDir;
    [ObservableProperty] private bool _subtitles;
    [ObservableProperty] private bool _sponsorBlock;
    [ObservableProperty] private bool _fullPlaylist;

    // yt-dlp status
    [ObservableProperty] private YtDlpStatus _ytDlpStatus = YtDlpStatus.Checking;
    [ObservableProperty] private string _ytDlpVersion = "";
    [ObservableProperty] private bool _ffmpegAvailable;

    // Dropdown sources
    public FormatOption[] FormatOptions => FormatOption.All;
    public QualityOption[] CurrentQualities => SelectedFormat?.IsVideo != false ? QualityOption.Video : QualityOption.Audio;

    public bool IsVideoFormat => SelectedFormat?.IsVideo != false;

    // Queue
    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = new();
    public bool HasJobs => Jobs.Count > 0;

    // Header display
    public string YtDlpStatusText => YtDlpStatus switch
    {
        YtDlpStatus.NotInstalled => "yt-dlp not installed",
        YtDlpStatus.Checking => "yt-dlp checking...",
        YtDlpStatus.Installing => "yt-dlp installing...",
        YtDlpStatus.Updating => "yt-dlp updating...",
        _ => $"yt-dlp {(string.IsNullOrEmpty(YtDlpVersion) ? "unknown" : YtDlpVersion)}"
    };

    public string YtDlpStatusColor => YtDlpStatus switch
    {
        YtDlpStatus.UpToDate => "#30D158",
        YtDlpStatus.UpdateAvailable => "#FF9F0A",
        YtDlpStatus.NotInstalled or YtDlpStatus.Error => "#FF453A",
        _ => "#8E8E93"
    };

    public string FfmpegStatusColor => FfmpegAvailable ? "#30D158" : "#FF453A";

    public string UpdateButtonText => YtDlpStatus switch
    {
        YtDlpStatus.NotInstalled => "Install yt-dlp",
        YtDlpStatus.UpdateAvailable => "Update yt-dlp",
        _ => "Check for updates"
    };

    public bool ShowUpdateButton => YtDlpStatus is not (YtDlpStatus.Checking or YtDlpStatus.Installing or YtDlpStatus.Updating);
    public bool IsChecking => YtDlpStatus is YtDlpStatus.Checking or YtDlpStatus.Installing or YtDlpStatus.Updating;

    partial void OnSelectedFormatChanged(FormatOption value)
    {
        SelectedQuality = value.IsVideo ? QualityOption.Video[0] : QualityOption.Audio[0];
        OnPropertyChanged(nameof(CurrentQualities));
        OnPropertyChanged(nameof(IsVideoFormat));
    }

    partial void OnYtDlpStatusChanged(YtDlpStatus value) => NotifyHeaderChanged();
    partial void OnYtDlpVersionChanged(string value) => NotifyHeaderChanged();
    partial void OnFfmpegAvailableChanged(bool value) => OnPropertyChanged(nameof(FfmpegStatusColor));

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(YtDlpStatusText));
        OnPropertyChanged(nameof(YtDlpStatusColor));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(IsChecking));
    }

    public void Initialize() => Task.Run(CheckYtDlpAsync);

    private async Task CheckYtDlpAsync()
    {
        Dispatcher.UIThread.Post(() => YtDlpStatus = YtDlpStatus.Checking);
        var (status, current, _, _) = await _svc.CheckStatusAsync();
        Dispatcher.UIThread.Post(() =>
        {
            YtDlpStatus = status;
            YtDlpVersion = current;
            FfmpegAvailable = _svc.IsFfmpegAvailable;
        });
    }

    [RelayCommand]
    private async Task UpdateYtDlpAsync()
    {
        if (YtDlpStatus is YtDlpStatus.NotInstalled or YtDlpStatus.UpdateAvailable)
        {
            YtDlpStatus = _svc.IsInstalled ? YtDlpStatus.Updating : YtDlpStatus.Installing;
            try
            {
                await _svc.InstallOrUpdateAsync();
                await CheckYtDlpAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => YtDlpStatus = YtDlpStatus.Error);
            }
        }
        else
        {
            await CheckYtDlpAsync();
        }
    }

    [RelayCommand]
    private void Paste()
    {
        try
        {
            var clip = TopLevel.GetTopLevel(
                ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow
            )?.Clipboard;
            if (clip != null)
            {
                var task = clip.GetTextAsync();
                task.ContinueWith(t =>
                {
                    if (t.Result is { } text && !string.IsNullOrWhiteSpace(text))
                        Dispatcher.UIThread.Post(() => Url = text.Trim());
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private void AddToQueue()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        foreach (var u in Url.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var job = new DownloadJobViewModel(u, this);
            Jobs.Insert(0, job);
        }
        Url = "";
        OnPropertyChanged(nameof(HasJobs));
        ProcessQueue();
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var window = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).MainWindow;
        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel == null) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Download Folder",
            AllowMultiple = false
        });
        if (folders.Count > 0) OutputDir = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void StartAll()
    {
        foreach (var job in Jobs)
            if (job.State is DownloadState.Queued or DownloadState.Cancelled or DownloadState.Error)
                job.Reset();
        ProcessQueue();
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.State is DownloadState.Downloading or DownloadState.Fetching or DownloadState.Processing))
            job.Cancel();
    }

    [RelayCommand]
    private void ClearFinished()
    {
        var toRemove = Jobs.Where(j => j.State is DownloadState.Finished or DownloadState.Cancelled).ToList();
        foreach (var j in toRemove) Jobs.Remove(j);
        OnPropertyChanged(nameof(HasJobs));
    }

    internal void ProcessQueue()
    {
        if (!_svc.IsInstalled) return;
        var slots = 3 - _activeDownloads;
        foreach (var job in Jobs.Where(j => j.State == DownloadState.Queued).Take(slots))
            StartDownload(job);
    }

    private void StartDownload(DownloadJobViewModel job)
    {
        _activeDownloads++;
        var cts = new CancellationTokenSource();
        job.Cts = cts;

        Task.Run(async () =>
        {
            try
            {
                await _svc.RunDownloadAsync(job.Url, SelectedFormat, SelectedQuality,
                    OutputDir, Subtitles, SponsorBlock, FullPlaylist,
                    update => Dispatcher.UIThread.Post(() => job.ApplyUpdate(update)),
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                Dispatcher.UIThread.Post(() => job.State = DownloadState.Cancelled);
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => { job.State = DownloadState.Error; job.Error = ex.Message; });
            }
            finally
            {
                _activeDownloads = Math.Max(0, _activeDownloads - 1);
                Dispatcher.UIThread.Post(ProcessQueue);
            }
        });
    }

    internal void RemoveJob(DownloadJobViewModel job)
    {
        job.Cancel();
        Jobs.Remove(job);
        OnPropertyChanged(nameof(HasJobs));
    }

    internal void RetryJob(DownloadJobViewModel job)
    {
        job.Reset();
        ProcessQueue();
    }
}

public partial class DownloadJobViewModel : ObservableObject
{
    private readonly MainViewModel _parent;
    public string Url { get; }
    internal CancellationTokenSource? Cts;

    [ObservableProperty] private string _title = "Fetching info...";
    [ObservableProperty] private DownloadState _state = DownloadState.Queued;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _speed = "";
    [ObservableProperty] private string _eta = "";
    [ObservableProperty] private string _error = "";

    public DownloadJobViewModel(string url, MainViewModel parent) { Url = url; _parent = parent; }

    // Display helpers
    public string StatusText => State.ToString();
    public string StatusBgColor => State switch
    {
        DownloadState.Queued or DownloadState.Cancelled => "#2C2C2E",
        DownloadState.Fetching or DownloadState.Downloading or DownloadState.Processing => "#1A3366",
        DownloadState.Finished => "#1A3D1A",
        DownloadState.Error => "#3D1A1A",
        _ => "#2C2C2E"
    };
    public string StatusFgColor => State switch
    {
        DownloadState.Queued or DownloadState.Cancelled => "#8E8E93",
        DownloadState.Fetching or DownloadState.Downloading or DownloadState.Processing => "#3478F6",
        DownloadState.Finished => "#30D158",
        DownloadState.Error => "#FF453A",
        _ => "#8E8E93"
    };

    public bool IsActive => State is DownloadState.Downloading or DownloadState.Processing;
    public bool CanCancel => State is DownloadState.Queued or DownloadState.Fetching or DownloadState.Downloading or DownloadState.Processing;
    public bool CanRetry => State is DownloadState.Error or DownloadState.Cancelled;
    public bool CanRemove => State is DownloadState.Finished or DownloadState.Error or DownloadState.Cancelled;
    public bool HasError => State == DownloadState.Error && !string.IsNullOrEmpty(Error);
    public string ProgressText => $"{(int)(Progress * 100)}%";

    partial void OnStateChanged(DownloadState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBgColor));
        OnPropertyChanged(nameof(StatusFgColor));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(ProgressText));

    public void ApplyUpdate(YtDlpService.ProgressUpdate u)
    {
        if (u.Title != null) Title = u.Title;
        if (u.Progress.HasValue) Progress = u.Progress.Value;
        if (u.Speed != null) Speed = u.Speed;
        if (u.Eta != null) Eta = u.Eta;
        if (u.State.HasValue) State = u.State.Value;
        if (u.Error != null) Error = u.Error;
    }

    public void Cancel() { Cts?.Cancel(); State = DownloadState.Cancelled; }
    public void Reset() { State = DownloadState.Queued; Progress = 0; Speed = ""; Eta = ""; Error = ""; }

    [RelayCommand] private void DoCancel() => Cancel();
    [RelayCommand] private void DoRetry() => _parent.RetryJob(this);
    [RelayCommand] private void DoRemove() => _parent.RemoveJob(this);
}
