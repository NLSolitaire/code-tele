using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDownloader.Core.Models;

namespace UberDownloader.Wpf.ViewModels;

public partial class JobCardViewModel : ObservableObject
{
    private readonly DownloadItem _item;
    private readonly Func<DownloadItem, Task> _onRetry;
    private readonly Func<DownloadItem, bool, Task> _onDelete;
    private readonly Action<DownloadItem> _onOpenFolder;
    private readonly Action<DownloadItem> _onOpenFile;
    private readonly Func<DownloadItem, Task>? _onPause;
    private readonly Func<DownloadItem, Task>? _onCancel;
    private readonly Func<DownloadItem, Task>? _onKeepClip;
    private readonly Func<DownloadItem, Task>? _onDeleteLive;
    private readonly Func<DownloadItem, Task>? _onManualExtract;
    private readonly Func<PlaylistJob, Task>? _onPausePlaylist;
    private readonly Func<PlaylistJob, Task>? _onResumePlaylist;
    private readonly Func<PlaylistJob, Task>? _onCancelPlaylist;
    private readonly Func<PlaylistJob, Task>? _onRetryFailedPlaylist;
    private CancellationTokenSource? _cts;

    public DownloadItem Item => _item;
    public string Id => _item.Id;
    public string Url => _item.Url;
    public DateTime CreatedAt => _item.CreatedAt;
    public UberDownloader.Core.Services.LocalizationService Loc => UberDownloader.Core.Services.LocalizationService.Instance;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private JobStatus _status;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _sizeStr = "--";

    [ObservableProperty]
    private string _speedStr = string.Empty;

    [ObservableProperty]
    private string _etaStr = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private string _liveDurationStr = string.Empty;

    [ObservableProperty]
    private bool _showDateSeparator;

    [ObservableProperty]
    private string _dateHeader = string.Empty;

    [ObservableProperty]
    private bool _isDetailsOpen;

    // --- Gestion des Playlists ---
    [ObservableProperty]
    private bool _isPlaylist;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _playlistSummaryText = string.Empty;

    public PlaylistJob? Playlist { get; set; }
    public System.Collections.ObjectModel.ObservableCollection<JobCardViewModel> ChildCards { get; } = new();

    public bool HasIndex => _item.PlaylistIndex > 0;
    public string IndexStr => _item.PlaylistIndex > 0 ? $"{_item.PlaylistIndex:D3}" : string.Empty;

    public int FinishedCount => ChildCards.Count(c => c.Status == JobStatus.Finished);
    public int DownloadingCount => ChildCards.Count(c => c.Status == JobStatus.Downloading || c.Status == JobStatus.Processing);
    public int QueuedCount => ChildCards.Count(c => c.Status == JobStatus.Queued);
    public int ErrorCount => ChildCards.Count(c => c.Status == JobStatus.Error);
    public int IgnoredCount => ChildCards.Count(c => c.Status == JobStatus.Ignored);
    public int TotalVideosCount => IsPlaylist ? (Playlist?.TotalItems ?? ChildCards.Count) : 0;
    public bool HasErrors => ErrorCount > 0;
    public string PlaylistVideosCountText => string.Format(Loc["PlaylistVideosCount"], TotalVideosCount);

    public int Attempts => _item.Attempts;
    public string LastError => !string.IsNullOrWhiteSpace(_item.LastError) ? _item.LastError : _item.ErrorMessage;
    public bool HasDiagnostics => Attempts > 0 || !string.IsNullOrWhiteSpace(LastError);
    public string DiagnosticsAttemptsText => string.Format(Loc["DiagnosticAttemptsValue"], Attempts);
    public string DiagnosticsLastErrorText => !string.IsNullOrWhiteSpace(LastError) ? LastError : Loc["DiagnosticNone"];

    public void RecalculatePlaylistStats()
    {
        if (!IsPlaylist) return;

        int fin = FinishedCount;
        int act = DownloadingCount;
        int que = QueuedCount;
        int err = ErrorCount;
        int ign = IgnoredCount;

        if (ChildCards.Count > 0)
        {
            Progress = Math.Round(ChildCards.Average(c => c.Progress), 1);
        }

        // Calcul de la vitesse globale et de l'ETA agrégé de la playlist (Étape 6)
        double aggregateSpeed = 0;
        long totalRemainingBytes = 0;

        var finishedItems = ChildCards.Where(c => c.Status == JobStatus.Finished && c.Item.TotalBytes > 0).ToList();
        long avgBytesPerVideo = finishedItems.Count > 0 
            ? (long)finishedItems.Average(c => c.Item.TotalBytes) 
            : 35L * 1024 * 1024; // Défaut 35 Mo

        foreach (var child in ChildCards)
        {
            if (child.Status == JobStatus.Downloading || child.Status == JobStatus.Processing)
            {
                aggregateSpeed += ParseSpeedBytesPerSecond(child.SpeedStr);
                if (child.Item.TotalBytes > child.Item.DownloadedBytes && child.Item.TotalBytes > 0)
                {
                    totalRemainingBytes += (child.Item.TotalBytes - child.Item.DownloadedBytes);
                }
                else if (child.Progress > 0 && child.Item.DownloadedBytes > 0)
                {
                    totalRemainingBytes += (long)(child.Item.DownloadedBytes * (100.0 - child.Progress) / child.Progress);
                }
                else
                {
                    totalRemainingBytes += avgBytesPerVideo;
                }
            }
            else if (child.Status == JobStatus.Queued)
            {
                totalRemainingBytes += avgBytesPerVideo;
            }
        }

        if (act > 0)
        {
            if (aggregateSpeed > 0)
            {
                SpeedStr = FormatBytesSpeed(aggregateSpeed);
                double seconds = totalRemainingBytes / aggregateSpeed;
                if (seconds >= 0 && seconds < 86400 * 7)
                {
                    var ts = TimeSpan.FromSeconds(seconds);
                    string durStr = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes:D2}m {ts.Seconds:D2}s";
                    EtaStr = string.Format(Loc["EtaEstimated"], durStr);
                }
                else
                {
                    EtaStr = Loc["EtaCalculating"];
                }
            }
            else
            {
                SpeedStr = string.Empty;
                EtaStr = Loc["EtaCalculating"];
            }
        }
        else
        {
            SpeedStr = string.Empty;
            EtaStr = string.Empty;
        }

        if (act == 0 && que == 0 && ChildCards.Count > 0)
        {
            Status = err > 0 ? JobStatus.Error : JobStatus.Finished;
            StatusDetail = string.Format(Loc["PlaylistCompletionReport"], fin, ign, err);
            PlaylistSummaryText = string.Format(Loc["PlaylistCompletionReport"], fin, ign, err);
        }
        else if (act > 0)
        {
            Status = JobStatus.Downloading;
            StatusDetail = string.Format(Loc["PlaylistCountDownloading"], act);
            PlaylistSummaryText = string.Format(Loc["PlaylistSummary"], fin, act, que, err);
        }
        else if (que > 0)
        {
            Status = JobStatus.Queued;
            StatusDetail = string.Format(Loc["PlaylistCountQueued"], que);
            PlaylistSummaryText = string.Format(Loc["PlaylistSummary"], fin, act, que, err);
        }
        else if (ChildCards.All(c => c.Status == JobStatus.Paused))
        {
            Status = JobStatus.Paused;
            StatusDetail = Loc["StatusPaused"];
            PlaylistSummaryText = string.Format(Loc["PlaylistSummary"], fin, act, que, err);
        }

        OnPropertyChanged(nameof(FinishedCount));
        OnPropertyChanged(nameof(DownloadingCount));
        OnPropertyChanged(nameof(QueuedCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(IgnoredCount));
        OnPropertyChanged(nameof(TotalVideosCount));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanPausePlaylist));
        OnPropertyChanged(nameof(CanResumePlaylist));
        OnPropertyChanged(nameof(CanCancelPlaylist));
        OnPropertyChanged(nameof(PlaylistVideosCountText));
        OnPropertyChanged(nameof(PlaylistSummaryText));
    }

    private static double ParseSpeedBytesPerSecond(string? speedStr)
    {
        if (string.IsNullOrWhiteSpace(speedStr)) return 0;
        var match = System.Text.RegularExpressions.Regex.Match(speedStr, @"([\d\.\,]+)\s*([KkMmGgTt]?[OoBb]/s|[KkMmGgTt]?[iI]?[Bb]/s)");
        if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            string unit = match.Groups[2].Value.ToUpperInvariant();
            if (unit.StartsWith("K")) return val * 1024.0;
            if (unit.StartsWith("M")) return val * 1024.0 * 1024.0;
            if (unit.StartsWith("G")) return val * 1024.0 * 1024.0 * 1024.0;
            return val;
        }
        return 0;
    }

    private static string FormatBytesSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} o/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} Ko/s";
        if (bytesPerSec < 1024 * 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):F1} Mo/s";
        return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):F2} Go/s";
    }

    public bool CanPausePlaylist => IsPlaylist && (Status == JobStatus.Downloading || Status == JobStatus.Queued || DownloadingCount > 0 || QueuedCount > 0);
    public bool CanResumePlaylist => IsPlaylist && (Status == JobStatus.Paused || (DownloadingCount == 0 && QueuedCount == 0 && ChildCards.Any(c => c.Status == JobStatus.Paused)));
    public bool CanCancelPlaylist => IsPlaylist && Status != JobStatus.Finished && Status != JobStatus.Cancelled;

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private async Task PausePlaylistAsync()
    {
        if (Playlist != null && _onPausePlaylist != null)
        {
            await _onPausePlaylist(Playlist);
            Status = JobStatus.Paused;
            StatusDetail = Loc["StatusPaused"];
        }
    }

    [RelayCommand]
    private async Task ResumePlaylistAsync()
    {
        if (Playlist != null && _onResumePlaylist != null)
        {
            await _onResumePlaylist(Playlist);
            Status = JobStatus.Downloading;
            StatusDetail = Loc["StatusDownloading"];
        }
    }

    [RelayCommand]
    private async Task RetryFailedPlaylistAsync()
    {
        if (Playlist != null && _onRetryFailedPlaylist != null)
        {
            await _onRetryFailedPlaylist(Playlist);
            Status = JobStatus.Downloading;
            StatusDetail = Loc["StatusDownloading"];
        }
    }

    [RelayCommand]
    private async Task CancelPlaylistAsync()
    {
        if (Playlist != null && _onCancelPlaylist != null)
        {
            await _onCancelPlaylist(Playlist);
            Status = JobStatus.Cancelled;
            StatusDetail = Loc["StatusCancelled"];
        }
    }

    public string CharacteristicsUrl => !string.IsNullOrWhiteSpace(_item.Url) ? _item.Url : "--";
    public string CharacteristicsResolution => !string.IsNullOrWhiteSpace(_item.VideoResolution) ? _item.VideoResolution : (!string.IsNullOrWhiteSpace(_item.Quality) ? _item.Quality : "Auto");
    public string CharacteristicsContainer
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_item.ContainerFormat) && !_item.ContainerFormat.Contains("Recommandé", StringComparison.OrdinalIgnoreCase))
                return _item.ContainerFormat.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(_item.OutputPath))
            {
                string ext = Path.GetExtension(_item.OutputPath).TrimStart('.').ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(ext)) return ext;
            }
            return "MP4";
        }
    }
    public string CharacteristicsCodec
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_item.VideoCodec) && !_item.VideoCodec.Contains("Auto", StringComparison.OrdinalIgnoreCase))
                return _item.VideoCodec;
            if (!string.IsNullOrWhiteSpace(_item.Codec))
                return _item.Codec;
            return "Auto (Défaut)";
        }
    }
    public string CharacteristicsAudio => FormatAudioSummary(_item.AudioLanguage);
    public string CharacteristicsSubtitles => FormatSubtitlesSummary(_item.SubtitleTrack);

    private static string FormatAudioSummary(string? audio)
    {
        var loc = UberDownloader.Core.Services.LocalizationService.Instance;
        if (string.IsNullOrWhiteSpace(audio) || audio.Equals("Originale", StringComparison.OrdinalIgnoreCase))
            return loc["AudioSingleOriginal"];
        if (audio.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return loc["AudioAllTracks"];
        var parts = audio.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 
            ? string.Format(loc["AudioMultipleTracks"], parts.Length, $"{string.Join(", ", parts.Take(3))}{(parts.Length > 3 ? "..." : "")}")
            : string.Format(loc["AudioSingleTrack"], audio);
    }

    private static string FormatSubtitlesSummary(string? subs)
    {
        var loc = UberDownloader.Core.Services.LocalizationService.Instance;
        if (string.IsNullOrWhiteSpace(subs) || subs.Equals("Aucun", StringComparison.OrdinalIgnoreCase))
            return loc["SubtitlesNone"];
        if (subs.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            return loc["SubtitlesAll"];
        var parts = subs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 
            ? string.Format(loc["SubtitlesMultiple"], parts.Length, $"{string.Join(", ", parts.Take(3))}{(parts.Length > 3 ? "..." : "")}")
            : string.Format(loc["SubtitlesSingle"], subs);
    }

    public bool IsLiveDownloading => IsLive && Status == JobStatus.Downloading;
    public bool IsLiveInterrupted => Status == JobStatus.LiveInterrupted;
    public bool IsProcessing => Status == JobStatus.Processing;

    public CancellationTokenSource? Cts
    {
        get => _cts;
        set => _cts = value;
    }

    public JobCardViewModel(
        DownloadItem item,
        Func<DownloadItem, Task> onRetry,
        Func<DownloadItem, bool, Task> onDelete,
        Action<DownloadItem> onOpenFolder,
        Action<DownloadItem> onOpenFile,
        Func<DownloadItem, Task>? onPause = null,
        Func<DownloadItem, Task>? onCancel = null,
        Func<DownloadItem, Task>? onKeepClip = null,
        Func<DownloadItem, Task>? onDeleteLive = null,
        Func<DownloadItem, Task>? onManualExtract = null,
        Func<PlaylistJob, Task>? onPausePlaylist = null,
        Func<PlaylistJob, Task>? onResumePlaylist = null,
        Func<PlaylistJob, Task>? onCancelPlaylist = null,
        Func<PlaylistJob, Task>? onRetryFailedPlaylist = null)
    {
        _item = item;
        _onRetry = onRetry;
        _onDelete = onDelete;
        _onOpenFolder = onOpenFolder;
        _onOpenFile = onOpenFile;
        _onPause = onPause;
        _onCancel = onCancel;
        _onKeepClip = onKeepClip;
        _onDeleteLive = onDeleteLive;
        _onManualExtract = onManualExtract;
        _onPausePlaylist = onPausePlaylist;
        _onResumePlaylist = onResumePlaylist;
        _onCancelPlaylist = onCancelPlaylist;
        _onRetryFailedPlaylist = onRetryFailedPlaylist;

        _title = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.Url);
        _status = item.Status;
        _statusDetail = item.StatusDetail;
        _progress = item.Progress;
        _sizeStr = item.SizeStr;
        _speedStr = item.SpeedStr;
        _etaStr = item.EtaStr;
        _errorMessage = item.ErrorMessage;
        _isLive = item.IsLive;
        _liveDurationStr = item.LiveDurationStr;
    }

    public void UpdateFromModel(DownloadItem item)
    {
        Title = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.Url);
        Status = item.Status;
        StatusDetail = item.StatusDetail;
        Progress = item.Progress;
        SizeStr = item.SizeStr;
        SpeedStr = item.SpeedStr;
        EtaStr = item.EtaStr;
        ErrorMessage = item.ErrorMessage;
        IsLive = item.IsLive;
        LiveDurationStr = item.LiveDurationStr;

        OnPropertyChanged(nameof(IsLiveDownloading));
        OnPropertyChanged(nameof(IsLiveInterrupted));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(CharacteristicsUrl));
        OnPropertyChanged(nameof(CharacteristicsResolution));
        OnPropertyChanged(nameof(CharacteristicsContainer));
        OnPropertyChanged(nameof(CharacteristicsCodec));
        OnPropertyChanged(nameof(CharacteristicsAudio));
        OnPropertyChanged(nameof(CharacteristicsSubtitles));
        OnPropertyChanged(nameof(Attempts));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticsAttemptsText));
        OnPropertyChanged(nameof(DiagnosticsLastErrorText));
    }

    [RelayCommand]
    private void StopLive()
    {
        _item.IsStopLiveRequested = true;
        Status = JobStatus.Processing;
        StatusDetail = Loc["StatusProcessing"];
        OnPropertyChanged(nameof(IsLiveDownloading));
        OnPropertyChanged(nameof(IsProcessing));
    }

    [RelayCommand]
    private async Task KeepClipAsync()
    {
        if (_onKeepClip != null)
        {
            await _onKeepClip(_item);
        }
    }

    [RelayCommand]
    private async Task DeleteLiveAsync()
    {
        if (_onDeleteLive != null)
        {
            await _onDeleteLive(_item);
        }
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (_onPause != null)
        {
            await _onPause(_item);
        }
        else
        {
            try { _cts?.Cancel(); } catch { }
            _item.Status = JobStatus.Paused;
            Status = JobStatus.Paused;
            StatusDetail = Loc["StatusPaused"];
            SpeedStr = string.Empty;
            EtaStr = string.Empty;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusDetail));
            OnPropertyChanged(nameof(SpeedStr));
            OnPropertyChanged(nameof(EtaStr));
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (_onCancel != null)
        {
            await _onCancel(_item);
        }
        else
        {
            try { _cts?.Cancel(); } catch { }
            _item.Status = JobStatus.Cancelled;
            Status = JobStatus.Cancelled;
            StatusDetail = Loc["StatusCancelled"];
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusDetail));
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        try { _cts?.Cancel(); } catch { }
        IsDetailsOpen = false;
        await _onRetry(_item);
    }

    [RelayCommand]
    private async Task ManualExtractAsync()
    {
        if (_onManualExtract != null)
        {
            await _onManualExtract(_item);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        // Supprime seulement de la liste (sans supprimer les fichiers sur le disque)
        await _onDelete(_item, false);
    }

    [RelayCommand]
    private void OpenFile()
    {
        _onOpenFile(_item);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        _onOpenFolder(_item);
    }

    [RelayCommand]
    private void ToggleDetails()
    {
        IsDetailsOpen = !IsDetailsOpen;
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Url))
            {
                System.Windows.Clipboard.SetText(Url);
            }
        }
        catch { }
    }
}
