using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YtDlpGui.Wpf.Models;
using YtDlpGui.Wpf.Services;

namespace YtDlpGui.Wpf;

public partial class MainWindow : Window
{
    private static readonly string[] VideoFormats = { "Video (MP4)", "Video (MKV)", "Video (WEBM)" };
    private static readonly string[] AudioFormats = { "Audio (MP3)", "Audio (M4A)", "Audio (FLAC)", "Audio (WAV)", "Audio (OPUS)" };
    private static readonly string[] VideoQualities = { "Best", "4K", "1440p", "1080p", "720p", "480p", "360p" };
    private static readonly string[] AudioQualities = { "Best", "320kbps", "256kbps", "192kbps", "128kbps" };
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x73, 0x3D));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x5F, 0x00));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));

    private readonly YtDlpService _service = new();
    private UpdateStatus? _updateStatus;
    private bool _checkingUpdates;
    private bool _updating;
    private int _nextId;

    public ObservableCollection<DownloadJob> Jobs { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        FormatCombo.ItemsSource = VideoFormats.Concat(AudioFormats).ToArray();
        FormatCombo.SelectedIndex = 0;
        QualityCombo.ItemsSource = VideoQualities;
        QualityCombo.SelectedIndex = 0;
        OutputDirInput.Text = _service.GetDefaultDownloadDirectory();

        ApplyUpdateButtons();
        UpdateFooterStats();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RefreshInstalledVersionAsync();
        await RefreshUpdateStatusAsync();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshUpdateStatusAsync();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating || _checkingUpdates)
        {
            return;
        }

        _updating = true;
        ApplyUpdateButtons();
        UpdateStateText.Text = _updateStatus?.Installed == true ? "Updating yt-dlp..." : "Installing yt-dlp...";
        UpdateStateText.Foreground = WarningBrush;

        try
        {
            var version = await _service.InstallOrUpdateAsync();
            BinaryStateText.Text = $"yt-dlp {version}";
            BinaryStateText.Foreground = SuccessBrush;
        }
        catch (Exception ex)
        {
            UpdateStateText.Text = $"Install/update failed: {ex.Message}";
            UpdateStateText.Foreground = ErrorBrush;
        }
        finally
        {
            _updating = false;
            await RefreshUpdateStatusAsync();
        }
    }

    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var format = FormatCombo.SelectedItem?.ToString() ?? VideoFormats[0];
        var isAudio = format.StartsWith("Audio", StringComparison.OrdinalIgnoreCase);
        QualityCombo.ItemsSource = isAudio ? AudioQualities : VideoQualities;
        QualityCombo.SelectedIndex = 0;
    }

    private void UrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddJob();
        }
    }

    private void AddJobButton_Click(object sender, RoutedEventArgs e)
    {
        AddJob();
    }

    private void AddJob()
    {
        var url = UrlInput.Text?.Trim() ?? string.Empty;
        var output = OutputDirInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        _nextId += 1;
        var job = new DownloadJob(
            $"dl-{_nextId}",
            url,
            FormatCombo.SelectedItem?.ToString() ?? VideoFormats[0],
            QualityCombo.SelectedItem?.ToString() ?? "Best",
            output,
            SubtitlesCheck.IsChecked == true,
            SponsorBlockCheck.IsChecked == true,
            PlaylistCheck.IsChecked == true);
        job.PropertyChanged += Job_PropertyChanged;
        Jobs.Add(job);

        UrlInput.Text = string.Empty;
        UpdateFooterStats();
    }

    private void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in Jobs.ToList())
        {
            if (job.Status == DownloadStatus.Queued)
            {
                _ = StartJobAsync(job);
            }
        }
    }

    private void ClearDoneButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in Jobs.Where(j => j.IsTerminal).ToList())
        {
            job.PropertyChanged -= Job_PropertyChanged;
            Jobs.Remove(job);
        }
        UpdateFooterStats();
    }

    private void CancelJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadJob job })
        {
            return;
        }

        job.Cancellation?.Cancel();
        if (job.Status == DownloadStatus.Queued)
        {
            job.Status = DownloadStatus.Cancelled;
        }
    }

    private void StartJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadJob job })
        {
            return;
        }

        if (job.Status == DownloadStatus.Queued)
        {
            _ = StartJobAsync(job);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadJob job })
        {
            return;
        }

        if (job.Status == DownloadStatus.Finished || Directory.Exists(job.OutputDirectory))
        {
            _service.OpenFolder(job.OutputDirectory);
        }
    }

    private void RemoveJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadJob job })
        {
            return;
        }

        job.Cancellation?.Cancel();
        job.PropertyChanged -= Job_PropertyChanged;
        Jobs.Remove(job);
        UpdateFooterStats();
    }

    private async Task StartJobAsync(DownloadJob job)
    {
        if (job.IsActive)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        job.Cancellation = cts;

        try
        {
            await _service.RunDownloadAsync(
                job,
                progress => Dispatcher.Invoke(() => ApplyProgress(job, progress)),
                cts.Token);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                job.Status = DownloadStatus.Error;
                job.Error = ex.Message;
                UpdateFooterStats();
            });
        }
        finally
        {
            cts.Dispose();
            job.Cancellation = null;
        }
    }

    private void ApplyProgress(DownloadJob job, DownloadProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.Title))
        {
            job.Title = progress.Title;
        }

        job.Progress = Math.Clamp(progress.Percent, 0, 100);
        job.Speed = progress.Speed;
        job.Eta = progress.Eta;
        job.Error = progress.Error;
        job.Status = ParseStatus(progress.Status);

        UpdateFooterStats();
    }

    private static DownloadStatus ParseStatus(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "queued" => DownloadStatus.Queued,
            "fetching" => DownloadStatus.Fetching,
            "downloading" => DownloadStatus.Downloading,
            "processing" => DownloadStatus.Processing,
            "finished" => DownloadStatus.Finished,
            "error" => DownloadStatus.Error,
            "cancelled" => DownloadStatus.Cancelled,
            _ => DownloadStatus.Queued,
        };
    }

    private async Task RefreshInstalledVersionAsync()
    {
        var version = await _service.GetInstalledVersionAsync();
        if (string.IsNullOrWhiteSpace(version))
        {
            BinaryStateText.Text = "yt-dlp missing";
            BinaryStateText.Foreground = ErrorBrush;
            return;
        }

        BinaryStateText.Text = $"yt-dlp {version}";
        BinaryStateText.Foreground = SuccessBrush;
    }

    private async Task RefreshUpdateStatusAsync()
    {
        _checkingUpdates = true;
        ApplyUpdateButtons();
        UpdateStateText.Text = "Checking latest release...";
        UpdateStateText.Foreground = WarningBrush;

        var status = await _service.CheckForUpdateAsync();
        _updateStatus = status;
        _checkingUpdates = false;
        ApplyUpdateButtons();

        if (!string.IsNullOrWhiteSpace(status.Error))
        {
            UpdateStateText.Text = "Could not verify latest version";
            UpdateStateText.Foreground = WarningBrush;
            return;
        }

        if (!status.Installed)
        {
            UpdateStateText.Text = !string.IsNullOrWhiteSpace(status.LatestVersion)
                ? $"yt-dlp missing (latest: {status.LatestVersion})"
                : "yt-dlp missing";
            UpdateStateText.Foreground = ErrorBrush;
            return;
        }

        if (status.UpdateAvailable)
        {
            UpdateStateText.Text = $"Update available: {status.CurrentVersion} -> {status.LatestVersion}";
            UpdateStateText.Foreground = WarningBrush;
            return;
        }

        UpdateStateText.Text = $"Up to date ({status.CurrentVersion})";
        UpdateStateText.Foreground = SuccessBrush;
    }

    private void ApplyUpdateButtons()
    {
        CheckUpdatesButton.IsEnabled = !_checkingUpdates && !_updating;
        CheckUpdatesButton.Content = _checkingUpdates ? "Checking..." : "Check updates";

        var canInstallOrUpdate = !_checkingUpdates
            && !_updating
            && (_updateStatus is null || !_updateStatus.Installed || _updateStatus.UpdateAvailable);

        UpdateButton.IsEnabled = canInstallOrUpdate;

        UpdateButton.Content = _updating
            ? (_updateStatus?.Installed == true ? "Updating..." : "Installing...")
            : _updateStatus is null || !_updateStatus.Installed
                ? "Install yt-dlp"
                : _updateStatus.UpdateAvailable
                    ? "Update yt-dlp"
                    : "Up to date";
    }

    private void Job_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadJob.Status))
        {
            UpdateFooterStats();
        }
    }

    private void UpdateFooterStats()
    {
        var active = Jobs.Count(j => j.IsActive);
        var queued = Jobs.Count(j => j.Status == DownloadStatus.Queued);
        var done = Jobs.Count(j => j.Status == DownloadStatus.Finished);
        var failed = Jobs.Count(j => j.Status == DownloadStatus.Error);

        var bits = new List<string>();
        if (active > 0)
        {
            bits.Add($"{active} active");
        }
        if (queued > 0)
        {
            bits.Add($"{queued} queued");
        }
        if (done > 0)
        {
            bits.Add($"{done} done");
        }
        if (failed > 0)
        {
            bits.Add($"{failed} failed");
        }

        FooterStatsText.Text = bits.Count == 0 ? "Ready" : string.Join(" | ", bits);
    }
}

