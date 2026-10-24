using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDownloader.Core.Data;
using UberDownloader.Core.Models;
using UberDownloader.Core.Services;

namespace UberDownloader.Wpf.ViewModels;

public partial class PlaylistPreparationViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly DuplicateDetectionService _duplicateService;
    private readonly SettingsService _settingsService;

    public LocalizationService Loc => LocalizationService.Instance;

    public PlaylistPreparationViewModel(
        DatabaseService databaseService, 
        DuplicateDetectionService duplicateService,
        SettingsService settingsService)
    {
        _databaseService = databaseService;
        _duplicateService = duplicateService;
        _settingsService = settingsService;
    }

    [ObservableProperty]
    private string _playlistTitle = string.Empty;

    [ObservableProperty]
    private string _customTitle = string.Empty;

    [ObservableProperty]
    private string _playlistUrl = string.Empty;

    [ObservableProperty]
    private string _targetFolder = string.Empty;

    [ObservableProperty]
    private int _rangeFrom = 1;

    [ObservableProperty]
    private int _rangeTo = 1;

    [ObservableProperty]
    private bool _isVideoMode = true;

    [ObservableProperty]
    private bool _isAudioMode;

    [ObservableProperty]
    private string _selectedResolution = "1080p (Full HD)";

    [ObservableProperty]
    private string _selectedAudioTier = "Standard — MP3 — 128 kb/s";

    [ObservableProperty]
    private string _selectedContainer = "Recommandé";

    [ObservableProperty]
    private bool _embedSubtitles = true;

    [ObservableProperty]
    private string _selectedNamingTemplate = "{index} - {title}";

    [ObservableProperty]
    private bool _isKnownPlaylist;

    [ObservableProperty]
    private int _newVideosCount;

    public bool HasNewVideos => NewVideosCount > 0;

    partial void OnNewVideosCountChanged(int value) => OnPropertyChanged(nameof(HasNewVideos));

    [ObservableProperty]
    private string _knownPlaylistBannerText = string.Empty;

    [ObservableProperty]
    private string _selectionRatioText = string.Empty;

    [ObservableProperty]
    private string _totalDurationText = string.Empty;

    [ObservableProperty]
    private string _estimatedTotalSizeText = string.Empty;

    [ObservableProperty]
    private string _freeDiskSpaceText = string.Empty;

    [ObservableProperty]
    private bool _isDiskSpaceWarningVisible;

    [ObservableProperty]
    private string _diskSpaceWarningText = string.Empty;

    [ObservableProperty]
    private string _downloadButtonText = string.Empty;

    [ObservableProperty]
    private bool _canDownload;

    public ObservableCollection<PlaylistPreparationItemViewModel> Entries { get; } = new();

    public ObservableCollection<string> AvailableResolutions { get; } = new()
    {
        "1080p (Full HD)",
        "720p (HD)",
        "480p",
        "360p"
    };

    public ObservableCollection<string> AudioTierOptions { get; } = new()
    {
        "Économique — MP3 — 96 kb/s",
        "Standard — MP3 — 128 kb/s",
        "Bonne qualité — AAC — 256 kb/s",
        "Très haute qualité — MP3 — 320 kb/s",
        "Sans perte — FLAC — qualité CD, 16 bits / 44,1 kHz",
        "Hi-Res — FLAC — 24 bits / 96 kHz ou plus, selon la source"
    };

    public List<string> AvailableContainersList { get; } = new()
    {
        "Recommandé", "MP4", "MKV", "WebM"
    };

    public List<string> AvailableNamingTemplates { get; } = new()
    {
        "{index} - {title}",
        "{title}",
        "{playlist} - {index} - {title}",
        "{id}"
    };

    public Action<PlaylistPreparationViewModel>? OnStartDownload { get; set; }
    public Action? OnCancel { get; set; }

    public int TotalCount => Entries.Count;
    public int SelectedCount => Entries.Count(e => e.IsSelected && e.IsAvailable);

    public void LoadPlaylist(PlaylistInspectionResult result, string downloadBaseFolder, string customTitle = "")
    {
        Entries.Clear();

        PlaylistUrl = result.SourceUrl;
        CustomTitle = customTitle;
        PlaylistTitle = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : result.Title;
        string safeTitle = DownloadService.SanitizeFileName(PlaylistTitle);
        TargetFolder = Path.Combine(downloadBaseFolder, safeTitle);

        IsKnownPlaylist = false;
        NewVideosCount = 0;
        KnownPlaylistBannerText = string.Empty;

        SelectedNamingTemplate = !string.IsNullOrWhiteSpace(_settingsService.Settings.PlaylistNamingTemplate)
            ? _settingsService.Settings.PlaylistNamingTemplate
            : "{index} - {title}";

        foreach (var entry in result.Entries)
        {
            var itemVm = new PlaylistPreparationItemViewModel
            {
                Index = entry.Index,
                Id = entry.Id,
                Title = entry.Title,
                Url = entry.Url,
                DurationSeconds = entry.DurationSeconds,
                ThumbnailUrl = entry.ThumbnailUrl,
                IsAvailable = entry.IsAvailable,
                UnavailableReason = entry.UnavailableReason,
                IsSelected = entry.IsAvailable // Par défaut, toutes les vidéos disponibles sont cochées
            };
            itemVm.SelectionChanged = RecalculateStats;
            Entries.Add(itemVm);
        }

        RangeFrom = 1;
        RangeTo = Math.Max(1, Entries.Count);

        RecalculateStats();

        // Analyse intelligente des playlists déjà connues et des doublons (Étape 6)
        _ = ScanDuplicatesAndKnownPlaylistAsync();
    }

    public async Task ScanDuplicatesAndKnownPlaylistAsync()
    {
        try
        {
            // 1. Détection si la playlist a déjà été enregistrée ou téléchargée
            var known = await _databaseService.GetPlaylistBySourceUrlAsync(PlaylistUrl);
            if (known != null)
            {
                IsKnownPlaylist = true;

                if (!string.IsNullOrWhiteSpace(known.NamingTemplate))
                {
                    SelectedNamingTemplate = known.NamingTemplate;
                }
                if (!string.IsNullOrWhiteSpace(known.TargetFolder) && Directory.Exists(known.TargetFolder))
                {
                    TargetFolder = known.TargetFolder;
                }
                if (!string.IsNullOrWhiteSpace(known.PreferredQuality))
                {
                    if (IsAudioMode) SelectedAudioTier = known.PreferredQuality;
                    else SelectedResolution = known.PreferredQuality;
                }

                var previousItems = await _databaseService.GetPlaylistItemsAsync(known.Id);
                var previousKeyMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var finishedKeyMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var pi in previousItems)
                {
                    string key = DuplicateDetectionService.ExtractVideoKey(pi.Url);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        previousKeyMap.Add(key);
                        if (pi.Status == JobStatus.Finished || (!string.IsNullOrWhiteSpace(pi.OutputPath) && File.Exists(pi.OutputPath)))
                        {
                            finishedKeyMap.Add(key);
                        }
                    }
                }

                foreach (var item in Entries)
                {
                    if (!item.IsAvailable) continue;

                    string key = DuplicateDetectionService.ExtractVideoKey(item.Url);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        if (previousKeyMap.Contains(key))
                        {
                            // Vidéo déjà présente dans la version précédente
                            item.IsNewInPlaylist = false;
                            if (finishedKeyMap.Contains(key))
                            {
                                item.IsAlreadyDownloaded = true;
                                item.IsSelected = false; // Décoché par défaut pour ne pas retélécharger
                            }
                        }
                        else
                        {
                            // Vidéo nouvellement ajoutée à la playlist !
                            item.IsNewInPlaylist = true;
                            item.IsSelected = true;
                        }
                    }
                }

                int newCount = Entries.Count(e => e.IsNewInPlaylist && e.IsAvailable);
                NewVideosCount = newCount;

                if (newCount > 0)
                {
                    KnownPlaylistBannerText = string.Format(Loc["KnownPlaylistWithNewVideosBanner"], newCount);
                }
                else
                {
                    KnownPlaylistBannerText = Loc["KnownPlaylistAllUpToDateBanner"];
                }
            }

            // 2. Analyse des doublons et conflits physiques sur le disque
            await ScanDuplicatesAsync();
        }
        catch (Exception ex)
        {
            Logger.Log(Loc.Format("LogPlaylistInspectError", ex.Message), "WARNING");
        }
    }

    public async Task ScanDuplicatesAsync()
    {
        string quality = IsAudioMode ? SelectedAudioTier : SelectedResolution;
        foreach (var item in Entries)
        {
            if (!item.IsAvailable) continue;

            var dupResult = await _duplicateService.CheckDuplicateAsync(
                item.Url, item.Title, item.Index, TargetFolder, quality, _databaseService);

            if (dupResult.Status != DuplicateStatus.None)
            {
                item.HasDuplicateConflict = true;
                item.ConflictStatus = dupResult.Status;
                item.ConflictBadgeText = dupResult.BadgeText;
                item.ExistingFilePath = dupResult.ExistingFilePath;

                if (dupResult.ShouldDeselectByDefault)
                {
                    item.IsSelected = false; // Décoché par défaut si déjà présent en qualité égale ou supérieure
                }
            }
        }

        RecalculateStats();
    }

    [RelayCommand]
    private void SelectOnlyNewVideos()
    {
        foreach (var item in Entries)
        {
            if (item.IsAvailable)
            {
                item.IsSelected = item.IsNewInPlaylist;
            }
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void DeselectExistingDuplicates()
    {
        foreach (var item in Entries)
        {
            if (item.HasDuplicateConflict && item.ConflictStatus == DuplicateStatus.IdenticalOrHigherQuality)
            {
                item.IsSelected = false;
            }
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void SelectVideoMode()
    {
        IsVideoMode = true;
        IsAudioMode = false;
        RecalculateStats();
    }

    [RelayCommand]
    private void SelectAudioMode()
    {
        IsVideoMode = false;
        IsAudioMode = true;
        RecalculateStats();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Entries)
        {
            if (item.IsAvailable)
            {
                item.IsSelected = true;
            }
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in Entries)
        {
            item.IsSelected = false;
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var item in Entries)
        {
            if (item.IsAvailable)
            {
                item.IsSelected = !item.IsSelected;
            }
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void ApplyRange()
    {
        int min = Math.Min(RangeFrom, RangeTo);
        int max = Math.Max(RangeFrom, RangeTo);

        foreach (var item in Entries)
        {
            if (item.IsAvailable)
            {
                item.IsSelected = item.Index >= min && item.Index <= max;
            }
        }
        RecalculateStats();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Loc["DestinationFolder"],
                InitialDirectory = Directory.Exists(TargetFolder) ? TargetFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                TargetFolder = dlg.FolderName;
                RecalculateStats();
            }
        }
        catch { }
    }

    [RelayCommand]
    private void StartDownload()
    {
        if (SelectedCount == 0) return;
        OnStartDownload?.Invoke(this);
    }

    [RelayCommand]
    private void Cancel()
    {
        OnCancel?.Invoke();
    }

    partial void OnSelectedResolutionChanged(string value) => RecalculateStats();
    partial void OnSelectedAudioTierChanged(string value) => RecalculateStats();
    partial void OnTargetFolderChanged(string value) => RecalculateStats();

    public void RecalculateStats()
    {
        int selected = SelectedCount;
        int total = TotalCount;
        CanDownload = selected > 0;

        SelectionRatioText = string.Format(Loc["SelectedVideosRatio"], selected, total);
        DownloadButtonText = string.Format(Loc["DownloadSelectedButton"], selected);

        // Durée totale sélectionnée
        double totalSeconds = Entries.Where(e => e.IsSelected && e.IsAvailable && e.DurationSeconds.HasValue)
                                     .Sum(e => e.DurationSeconds!.Value);
        var ts = TimeSpan.FromSeconds(totalSeconds);
        string durStr = ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s"
            : $"{ts.Minutes:D2}m {ts.Seconds:D2}s";
        TotalDurationText = string.Format(Loc["DurationTotal"], durStr);

        // Estimation taille totale
        double estimatedBytes = 0;
        double totalMinutes = totalSeconds > 0 ? totalSeconds / 60.0 : selected * 4.0; // défaut 4 min si durée inconnue

        if (IsAudioMode)
        {
            double mbPerMin = SelectedAudioTier.Contains("320") ? 2.4 :
                             (SelectedAudioTier.Contains("FLAC") || SelectedAudioTier.Contains("Hi-Res") ? 5.5 : 1.2);
            estimatedBytes = totalMinutes * mbPerMin * 1024 * 1024;
        }
        else
        {
            double mbPerMin = SelectedResolution.Contains("2160") || SelectedResolution.Contains("4k") ? 45.0 :
                             (SelectedResolution.Contains("1440") ? 28.0 :
                             (SelectedResolution.Contains("1080") ? 18.0 :
                             (SelectedResolution.Contains("720") ? 9.0 : 4.5)));
            estimatedBytes = totalMinutes * mbPerMin * 1024 * 1024;
        }

        EstimatedTotalSizeText = FormatBytes(estimatedBytes);

        // Espace disque restant
        CheckDiskSpace(estimatedBytes);

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    private void CheckDiskSpace(double requiredBytes)
    {
        try
        {
            string pathToCheck = !string.IsNullOrWhiteSpace(TargetFolder) ? TargetFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root = Path.GetPathRoot(Path.GetFullPath(pathToCheck)) ?? "C:\\";
            var drive = new DriveInfo(root);

            long free = drive.AvailableFreeSpace;
            FreeDiskSpaceText = string.Format(Loc["DiskSpaceAvailable"], FormatBytes(free));

            if (free < requiredBytes)
            {
                IsDiskSpaceWarningVisible = true;
                DiskSpaceWarningText = string.Format(Loc["DiskSpaceWarning"], FormatBytes(free), FormatBytes(requiredBytes));
            }
            else
            {
                IsDiskSpaceWarningVisible = false;
                DiskSpaceWarningText = string.Empty;
            }
        }
        catch
        {
            FreeDiskSpaceText = string.Empty;
            IsDiskSpaceWarningVisible = false;
            DiskSpaceWarningText = string.Empty;
        }
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024) return $"{bytes:F0} o";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} Ko";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} Mo";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} Go";
    }
}
