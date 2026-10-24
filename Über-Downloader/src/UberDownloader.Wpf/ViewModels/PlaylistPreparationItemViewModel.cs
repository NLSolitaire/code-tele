using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UberDownloader.Core.Services;

namespace UberDownloader.Wpf.ViewModels;

public partial class PlaylistPreparationItemViewModel : ObservableObject
{
    public int Index { get; set; }
    public string IndexStr => $"{Index:D3}";
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public string UnavailableReason { get; set; } = string.Empty;

    public string DurationStr
    {
        get
        {
            if (!DurationSeconds.HasValue || DurationSeconds.Value <= 0) return "--:--";
            var ts = TimeSpan.FromSeconds(DurationSeconds.Value);
            return ts.Hours > 0 ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasDuplicateConflict;

    [ObservableProperty]
    private string _conflictBadgeText = string.Empty;

    [ObservableProperty]
    private DuplicateStatus _conflictStatus = DuplicateStatus.None;

    [ObservableProperty]
    private string _existingFilePath = string.Empty;

    [ObservableProperty]
    private bool _isNewInPlaylist;

    [ObservableProperty]
    private bool _isAlreadyDownloaded;

    public string SelectionStateText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!IsAvailable) return loc["StatusIgnored"];
            if (IsSelected) return loc["StatusReady"];
            return loc["StatusUnselected"];
        }
    }

    public string SelectionStateColor
    {
        get
        {
            if (!IsAvailable) return "#EF4444";
            if (IsSelected) return "#10B981";
            return "#64748B";
        }
    }

    public Action? SelectionChanged { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionStateText));
        OnPropertyChanged(nameof(SelectionStateColor));
        SelectionChanged?.Invoke();
    }
}
