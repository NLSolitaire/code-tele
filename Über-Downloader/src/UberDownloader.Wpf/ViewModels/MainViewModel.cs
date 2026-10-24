using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDownloader.Core.Data;
using UberDownloader.Core.Models;
using UberDownloader.Core.Services;

namespace UberDownloader.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _databaseService;
    private readonly SettingsService _settingsService;
    private readonly MediaInspector _mediaInspector;
    private readonly DownloadService _downloadService;
    private readonly PlaylistService _playlistService;
    private readonly DownloadScheduler _downloadScheduler;
    private readonly DuplicateDetectionService _duplicateService;

    public SettingsViewModel SettingsVm { get; }
    public PlaylistPreparationViewModel PlaylistPrepVm { get; }

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isPlaylistPrepOpen;

    private PlaylistInspectionResult? _cachedPlaylistResult;

    [ObservableProperty]
    private bool _isSuccessNotificationVisible;

    [ObservableProperty]
    private string _successNotificationTitle = string.Empty;

    [ObservableProperty]
    private string _successNotificationMessage = string.Empty;

    [RelayCommand]
    private void DismissSuccessNotification()
    {
        IsSuccessNotificationVisible = false;
    }

    [ObservableProperty]
    private bool _isExitConfirmOpen;

    public bool HasActiveDownloads => _downloadScheduler.HasActiveDownloads;

    [ObservableProperty]
    private int _activeDownloadsCount;

    public void RefreshActiveDownloadsCount()
    {
        int count = _downloadScheduler.ActiveDownloadsCount;
        if (ActiveDownloadsCount != count)
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                ActiveDownloadsCount = count;
            }
            else
            {
                Application.Current?.Dispatcher?.BeginInvoke(() => ActiveDownloadsCount = count);
            }
        }
    }

    public Action? RequestHideWindow { get; set; }
    public Action? RequestExplicitShutdown { get; set; }

    [RelayCommand]
    private void ExitBackground()
    {
        IsExitConfirmOpen = false;
        RequestHideWindow?.Invoke();
    }

    [RelayCommand]
    private void ExitPauseAndQuit()
    {
        IsExitConfirmOpen = false;
        _downloadScheduler.PauseAll();
        RequestExplicitShutdown?.Invoke();
    }

    [RelayCommand]
    private void ExitCancelAndQuit()
    {
        IsExitConfirmOpen = false;
        _downloadScheduler.CancelAll();
        RequestExplicitShutdown?.Invoke();
    }

    [RelayCommand]
    private void ExitStay()
    {
        IsExitConfirmOpen = false;
    }

    [ObservableProperty]
    private string _inputUrl = string.Empty;

    [ObservableProperty]
    private string _customTitle = string.Empty;

    [ObservableProperty]
    private bool _isInspecting;

    [ObservableProperty]
    private string _inspectionStatusText = string.Empty;

    [ObservableProperty]
    private MediaInspectionResult? _currentInspection;

    // Choix Niveau 1 : Mode Vidéo ou Audio
    [ObservableProperty]
    private bool _isVideoMode = true;

    [ObservableProperty]
    private bool _isAudioMode;

    // Niveau 1 Vidéo : Résolution sélectionnée
    [ObservableProperty]
    private string _selectedResolution = "1080p (Full HD)";

    [ObservableProperty]
    private string _selectedAudioLanguage = "Originale";

    [ObservableProperty]
    private string _selectedSubtitle = "Aucun";

    // Multi-sélection Pistes audio
    public ObservableCollection<SelectableItem> AudioTracks { get; } = new();

    [ObservableProperty]
    private bool _isAudioDropDownOpen;

    [ObservableProperty]
    private string _selectedAudioSummary = "Originale";

    [ObservableProperty]
    private bool _isAllAudioSelected;

    // Multi-sélection Sous-titres
    public ObservableCollection<SelectableItem> SubtitleTracks { get; } = new();

    [ObservableProperty]
    private bool _isSubtitlesDropDownOpen;

    [ObservableProperty]
    private string _selectedSubtitlesSummary = "Aucun";

    [ObservableProperty]
    private bool _isAllSubtitlesSelected;

    [ObservableProperty]
    private bool _embedSubtitles = true;

    // Niveau 1 Audio
    [ObservableProperty]
    private string _selectedAudioTier = "Standard — MP3 — 128 kb/s";

    [ObservableProperty]
    private string _estimatedSizeText = string.Empty;

    [ObservableProperty]
    private bool _isAdvancedOpen;

    [ObservableProperty]
    private DateFilter _currentDateFilter = DateFilter.All;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasNoCards = true;

    // Réglages avancés par téléchargement (indépendants des réglages globaux)
    [ObservableProperty]
    private string _itemVideoCodec = "Automatique";

    [ObservableProperty]
    private string _itemVideoFps = "Conserver celle de la source";

    [ObservableProperty]
    private string _itemVideoDynamicRange = "Automatique";

    [ObservableProperty]
    private string _itemAudioCodec = "Automatique";

    [ObservableProperty]
    private string _itemAudioBitrate = "Automatique";

    [ObservableProperty]
    private string _itemAudioSampleRate = "Conserver celle de la source";

    [ObservableProperty]
    private string _itemAudioChannels = "Automatique";

    [ObservableProperty]
    private string _itemContainer = "Recommandé";

    [ObservableProperty]
    private string _itemSpeedLimit = "Illimité";

    public ObservableCollection<string> AvailableContainersList { get; } = new();
    public ObservableCollection<string> AvailableItemSpeedLimits { get; } = new();
    public ObservableCollection<string> AvailableVideoCodecsList { get; } = new();
    public ObservableCollection<string> AvailableVideoFpsList { get; } = new();
    public ObservableCollection<string> AvailableDynamicRangesList { get; } = new();
    public ObservableCollection<string> AvailableAudioCodecsList { get; } = new();
    public ObservableCollection<string> AvailableAudioBitratesList { get; } = new();
    public ObservableCollection<string> AvailableAudioSampleRatesList { get; } = new();
    public ObservableCollection<string> AvailableAudioChannelsList { get; } = new();
    public ObservableCollection<string> AudioTierOptions { get; } = new();

    public void RefreshMainLocalizedOptions()
    {
        ItemVideoCodec = Loc.TranslateOption(ItemVideoCodec);
        ItemVideoFps = Loc.TranslateOption(ItemVideoFps);
        ItemVideoDynamicRange = Loc.TranslateOption(ItemVideoDynamicRange);
        ItemAudioCodec = Loc.TranslateOption(ItemAudioCodec);
        ItemAudioBitrate = Loc.TranslateOption(ItemAudioBitrate);
        ItemAudioSampleRate = Loc.TranslateOption(ItemAudioSampleRate);
        ItemAudioChannels = Loc.TranslateOption(ItemAudioChannels);
        ItemContainer = Loc.TranslateOption(ItemContainer);
        ItemSpeedLimit = Loc.TranslateOption(ItemSpeedLimit);
        SelectedAudioTier = Loc.TranslateOption(SelectedAudioTier);

        ReplaceCollectionItems(AvailableContainersList,
            Loc["OptionRecommended"], "MP4", "MKV", "WebM");

        ReplaceCollectionItems(AvailableItemSpeedLimits,
            Loc["OptionUnlimited"], "500 Ko/s", "1 Mo/s", "5 Mo/s", "10 Mo/s", "20 Mo/s", "50 Mo/s");

        ReplaceCollectionItems(AvailableVideoCodecsList,
            Loc["OptionAutomatic"], "H.264/AVC", "H.265/HEVC", "VP9", "AV1");

        ReplaceCollectionItems(AvailableVideoFpsList,
            Loc["OptionKeepSource"], "24", "25", "30", "50", "60", "120");

        ReplaceCollectionItems(AvailableDynamicRangesList,
            Loc["OptionAutomatic"], "SDR", "HDR10", "Dolby Vision");

        ReplaceCollectionItems(AvailableAudioCodecsList,
            Loc["OptionAutomatic"], "AAC", "MP3", "Opus", "FLAC", "ALAC");

        ReplaceCollectionItems(AvailableAudioBitratesList,
            Loc["OptionAutomatic"], "96 kb/s", "128 kb/s", "192 kb/s", "256 kb/s", "320 kb/s");

        ReplaceCollectionItems(AvailableAudioSampleRatesList,
            Loc["OptionKeepSource"], "44,1 kHz", "48 kHz", "88,2 kHz", "96 kHz", "176,4 kHz", "192 kHz");

        ReplaceCollectionItems(AvailableAudioChannelsList,
            Loc["OptionAutomatic"], Loc["ChannelMono"], Loc["ChannelStereo"], Loc["ChannelMultichannel"]);

        ReplaceCollectionItems(AudioTierOptions,
            Loc["AudioTierEconomy"],
            Loc["AudioTierStandard"],
            Loc["AudioTierGood"],
            Loc["AudioTierHigh"],
            Loc["AudioTierLossless"],
            Loc["AudioTierHiRes"]);

        if (AvailableAudioLanguages.Count == 1 && (AvailableAudioLanguages[0].Equals("Originale", StringComparison.OrdinalIgnoreCase) || AvailableAudioLanguages[0].Equals("Original", StringComparison.OrdinalIgnoreCase)))
        {
            AvailableAudioLanguages[0] = Loc["OptionOriginal"];
            if (SelectedAudioLanguage.Equals("Originale", StringComparison.OrdinalIgnoreCase) || SelectedAudioLanguage.Equals("Original", StringComparison.OrdinalIgnoreCase))
            {
                SelectedAudioLanguage = Loc["OptionOriginal"];
            }
        }
        if (AvailableSubtitles.Count == 1 && (AvailableSubtitles[0].Equals("Aucun", StringComparison.OrdinalIgnoreCase) || AvailableSubtitles[0].Equals("None", StringComparison.OrdinalIgnoreCase) || AvailableSubtitles[0].Equals("Ninguno", StringComparison.OrdinalIgnoreCase)))
        {
            AvailableSubtitles[0] = Loc["OptionNone"];
            if (SelectedSubtitle.Equals("Aucun", StringComparison.OrdinalIgnoreCase) || SelectedSubtitle.Equals("None", StringComparison.OrdinalIgnoreCase) || SelectedSubtitle.Equals("Ninguno", StringComparison.OrdinalIgnoreCase))
            {
                SelectedSubtitle = Loc["OptionNone"];
            }
        }
        foreach (var track in AudioTracks)
        {
            if (track.DisplayName.Equals("Originale", StringComparison.OrdinalIgnoreCase) || track.DisplayName.Equals("Original", StringComparison.OrdinalIgnoreCase))
            {
                track.DisplayName = Loc["OptionOriginal"];
            }
        }

        UpdateAudioSummary();
        UpdateSubtitlesSummary();
    }

    private static void ReplaceCollectionItems(ObservableCollection<string> col, params string[] items)
    {
        col.Clear();
        foreach (var item in items)
        {
            col.Add(item);
        }
    }

    // Résolutions vidéo détectées
    public ObservableCollection<string> AvailableResolutions { get; } = new()
    {
        "1080p (Full HD)",
        "720p (HD)",
        "480p",
        "360p"
    };

    // Langues audio détectées
    public ObservableCollection<string> AvailableAudioLanguages { get; } = new()
    {
        "Originale"
    };

    // Sous-titres détectés
    public ObservableCollection<string> AvailableSubtitles { get; } = new()
    {
        "Aucun"
    };

    public ObservableCollection<JobCardViewModel> Cards { get; } = new();

    private CancellationTokenSource? _inspectCts;

    public MainViewModel(
        DatabaseService databaseService,
        SettingsService settingsService,
        MediaInspector mediaInspector,
        DownloadService downloadService,
        PlaylistService playlistService,
        DownloadScheduler downloadScheduler,
        DuplicateDetectionService duplicateService,
        SettingsViewModel settingsVm,
        PlaylistPreparationViewModel playlistPrepVm)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _mediaInspector = mediaInspector;
        _downloadService = downloadService;
        _playlistService = playlistService;
        _downloadScheduler = downloadScheduler;
        _duplicateService = duplicateService;
        SettingsVm = settingsVm;
        PlaylistPrepVm = playlistPrepVm;

        SettingsVm.RequestClose = () =>
        {
            IsSettingsOpen = false;
        };

        PlaylistPrepVm.OnCancel = () =>
        {
            IsPlaylistPrepOpen = false;
        };

        PlaylistPrepVm.OnStartDownload = (prepVm) =>
        {
            IsPlaylistPrepOpen = false;
            _ = LaunchPreparedPlaylistDownloadAsync(prepVm);
        };

        // Rafraîchir les libellés si la langue change
        Loc.LanguageChanged += () =>
        {
            RecalculateDateSeparators();
            RefreshMainLocalizedOptions();
        };

        RefreshMainLocalizedOptions();

        var activeCountTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        activeCountTimer.Tick += (s, e) => RefreshActiveDownloadsCount();
        activeCountTimer.Start();
    }

    public async Task InitializeAsync()
    {
        await LoadHistoryAsync();
        await HandleStartupQueueAsync();
    }

    private async Task HandleStartupQueueAsync()
    {
        bool autoResume = _settingsService.Settings.AutoResumeOnStartup;

        if (autoResume)
        {
            // 1. Playlists ayant des éléments interrompus ou en attente
            foreach (var card in Cards.Where(c => c.IsPlaylist && c.Playlist != null).ToList())
            {
                var pl = card.Playlist!;
                var itemsToResume = card.ChildCards
                    .Where(c => c.Status == JobStatus.Downloading || c.Status == JobStatus.Queued)
                    .Select(c => c.Item)
                    .ToList();

                if (itemsToResume.Count > 0)
                {
                    foreach (var it in itemsToResume)
                    {
                        it.Status = JobStatus.Queued;
                        it.StatusDetail = Loc["StatusQueued"];
                        var childVm = card.ChildCards.FirstOrDefault(c => c.Id == it.Id);
                        childVm?.UpdateFromModel(it);
                        await _databaseService.UpsertItemAsync(it);
                    }

                    pl.Status = JobStatus.Downloading;
                    await _databaseService.UpsertPlaylistAsync(pl);
                    card.RecalculatePlaylistStats();

                    _downloadScheduler.EnqueueRange(
                        itemsToResume,
                        onProgress: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var childVm = card.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                                childVm?.UpdateFromModel(updated);
                                card.RecalculatePlaylistStats();
                            });
                        },
                        onCompleted: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var childVm = card.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                                childVm?.UpdateFromModel(updated);
                                card.RecalculatePlaylistStats();
                                CheckPlaylistCompletion(card);
                            });
                        }
                    );

                    Logger.Log(Loc.Format("LogAutoResumePlaylist", pl.Title, itemsToResume.Count));
                }
            }

            // 2. Éléments indépendants (hors playlists)
            foreach (var card in Cards.Where(c => !c.IsPlaylist).ToList())
            {
                var it = card.Item;
                if (it.Status == JobStatus.Downloading || it.Status == JobStatus.Queued)
                {
                    it.Status = JobStatus.Queued;
                    it.StatusDetail = Loc["StatusQueued"];
                    card.UpdateFromModel(it);
                    await _databaseService.UpsertItemAsync(it);

                    _downloadScheduler.Enqueue(
                        it,
                        onProgress: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                card.UpdateFromModel(updated);
                            });
                        },
                        onCompleted: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                card.UpdateFromModel(updated);
                            });
                        }
                    );

                    Logger.Log(Loc.Format("LogAutoResumeItem", it.Title));
                }
            }
        }
        else
        {
            // AutoResume inactif : passer les téléchargements interrompus en pause
            foreach (var card in Cards.ToList())
            {
                if (card.IsPlaylist)
                {
                    bool anyModified = false;
                    foreach (var child in card.ChildCards.Where(c => c.Status == JobStatus.Downloading))
                    {
                        child.Item.Status = JobStatus.Paused;
                        child.Item.StatusDetail = Loc["StatusPaused"];
                        child.UpdateFromModel(child.Item);
                        await _databaseService.UpsertItemAsync(child.Item);
                        anyModified = true;
                    }
                    if (card.Status == JobStatus.Downloading || (anyModified && card.ChildCards.All(c => c.Status == JobStatus.Paused || c.Status == JobStatus.Finished || c.Status == JobStatus.Ignored)))
                    {
                        if (card.Playlist != null)
                        {
                            card.Playlist.Status = JobStatus.Paused;
                            await _databaseService.UpsertPlaylistAsync(card.Playlist);
                        }
                    }
                    card.RecalculatePlaylistStats();
                }
                else if (card.Status == JobStatus.Downloading)
                {
                    card.Item.Status = JobStatus.Paused;
                    card.Item.StatusDetail = Loc["StatusPaused"];
                    card.UpdateFromModel(card.Item);
                    await _databaseService.UpsertItemAsync(card.Item);
                }
            }
        }
    }

    [RelayCommand]
    private void SelectVideoMode()
    {
        IsVideoMode = true;
        IsAudioMode = false;
        UpdateEstimatedSize();
    }

    [RelayCommand]
    private void SelectAudioMode()
    {
        IsVideoMode = false;
        IsAudioMode = true;
        UpdateEstimatedSize();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SettingsVm.LoadSettings();
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void OpenKofi()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/uberdownloader",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[KOFI] Impossible d'ouvrir le lien Ko-fi : {ex.Message}");
        }
    }

    partial void OnInputUrlChanged(string value)
    {
        _cachedPlaylistResult = null;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _inspectCts?.Cancel();
            IsInspecting = false;
            CurrentInspection = null;
            InspectionStatusText = string.Empty;
            EstimatedSizeText = string.Empty;
            CustomTitle = string.Empty; // Supprime le titre simultanément dès que le lien est effacé de la barre
            AvailableResolutions.Clear();
            AvailableAudioLanguages.Clear();
            AudioTracks.Clear();
            SubtitleTracks.Clear();
            SelectedAudioSummary = "Originale";
            SelectedSubtitlesSummary = "Aucun";
            return;
        }

        // Quand le lien rentré change, on réinitialise le titre et on relance la vérification/inspection
        CustomTitle = string.Empty;
        TriggerInspection(value.Trim());
    }

    private void TriggerInspection(string url)
    {
        _inspectCts?.Cancel();
        _inspectCts = new CancellationTokenSource();
        var token = _inspectCts.Token;

        IsInspecting = true;
        bool isPlaylist = _playlistService.IsPlaylistUrl(url);
        InspectionStatusText = isPlaylist
            ? LocalizationService.Instance["PlaylistInspecting"]
            : LocalizationService.Instance["InspectingFormats"];

        Task.Run(async () =>
        {
            try
            {
                // Légère pause pour éviter de multiplier les inspections lors de saisies rapides
                await Task.Delay(250, token);
                if (token.IsCancellationRequested) return;

                if (isPlaylist)
                {
                    var plResult = await _playlistService.InspectPlaylistAsync(url, token);
                    if (token.IsCancellationRequested) return;

                    if (plResult != null && plResult.Entries.Count > 0)
                    {
                        _cachedPlaylistResult = plResult;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsInspecting = false;
                            InspectionStatusText = string.Empty;
                            if (!string.IsNullOrWhiteSpace(plResult.Title))
                            {
                                CustomTitle = plResult.Title;
                            }
                            EstimatedSizeText = string.Format(LocalizationService.Instance["PlaylistVideosCount"], plResult.Entries.Count);
                        });
                        return;
                    }
                }

                var result = await _mediaInspector.InspectUrlAsync(url, token);
                if (token.IsCancellationRequested) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentInspection = result;
                    IsInspecting = false;
                    InspectionStatusText = string.Empty;

                    // Quand le lien rentré change, le titre doit être revérifié
                    if (!string.IsNullOrWhiteSpace(result.Title))
                    {
                        CustomTitle = result.Title;
                    }

                    // Mise à jour des résolutions disponibles
                    AvailableResolutions.Clear();
                    foreach (var res in result.AvailableResolutions)
                    {
                        AvailableResolutions.Add(res);
                    }
                    SelectedResolution = AvailableResolutions.FirstOrDefault() ?? "1080p (Full HD)";

                    // Mise à jour des langues audio disponibles
                    AvailableAudioLanguages.Clear();
                    AudioTracks.Clear();
                    foreach (var lang in result.AvailableAudioLanguages)
                    {
                        AvailableAudioLanguages.Add(lang);
                        var item = new SelectableItem
                        {
                            DisplayName = lang.Equals("Originale", StringComparison.OrdinalIgnoreCase) ? Loc["OptionOriginal"] : lang,
                            Code = DownloadService.ExtractLanguageCode(lang),
                            IsSelected = (lang.Equals("Originale", StringComparison.OrdinalIgnoreCase) || AudioTracks.Count == 0)
                        };
                        item.SelectionChanged = UpdateAudioSummary;
                        AudioTracks.Add(item);
                    }
                    SelectedAudioLanguage = AvailableAudioLanguages.FirstOrDefault() ?? Loc["OptionOriginal"];
                    IsAllAudioSelected = false;
                    UpdateAudioSummary();

                    // Mise à jour des sous-titres disponibles
                    AvailableSubtitles.Clear();
                    SubtitleTracks.Clear();
                    var distinctSubNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var sub in result.AvailableSubtitles)
                    {
                        AvailableSubtitles.Add(sub);
                        if (sub.Equals("Aucun", StringComparison.OrdinalIgnoreCase)) continue;
                        string cleanName = sub.Replace(" (intégré)", "")
                                              .Replace(" (.srt séparé)", "")
                                              .Replace(" (auto .srt)", " (auto)")
                                              .Trim();
                        if (distinctSubNames.Add(cleanName))
                        {
                            var item = new SelectableItem
                            {
                                DisplayName = cleanName,
                                Code = DownloadService.ExtractLanguageCode(cleanName),
                                IsSelected = false,
                                IsAuto = cleanName.Contains("(auto)")
                            };
                            item.SelectionChanged = UpdateSubtitlesSummary;
                            SubtitleTracks.Add(item);
                        }
                    }
                    SelectedSubtitle = AvailableSubtitles.FirstOrDefault() ?? Loc["OptionNone"];
                    IsAllSubtitlesSelected = false;
                    UpdateSubtitlesSummary();

                    UpdateEstimatedSize();
                });
            }
            catch
            {
                if (!token.IsCancellationRequested)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsInspecting = false;
                        InspectionStatusText = string.Empty;
                    });
                }
            }
        }, token);
    }

    partial void OnSelectedAudioTierChanged(string value)
    {
        UpdateEstimatedSize();
    }

    partial void OnSelectedResolutionChanged(string value)
    {
        UpdateEstimatedSize();
    }

    private void UpdateEstimatedSize()
    {
        if (CurrentInspection == null)
        {
            EstimatedSizeText = string.Empty;
            return;
        }

        if (IsAudioMode)
        {
            string tier = SelectedAudioTier.ToLowerInvariant();
            if (tier.Contains("96")) EstimatedSizeText = "≈ 3.5 Mo";
            else if (tier.Contains("128")) EstimatedSizeText = "≈ 4.8 Mo";
            else if (tier.Contains("256")) EstimatedSizeText = "≈ 9.2 Mo";
            else if (tier.Contains("320")) EstimatedSizeText = "≈ 12 Mo";
            else if (tier.Contains("qualité cd") || tier.Contains("sans perte")) EstimatedSizeText = "≈ 28 Mo";
            else if (tier.Contains("hi-res")) EstimatedSizeText = "≈ 75 Mo";
            else EstimatedSizeText = "≈ 5 Mo";
        }
        else
        {
            string res = SelectedResolution.ToLowerInvariant();
            if (res.Contains("4320") || res.Contains("8k")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("4320p", "≈ 1.5 Go");
            else if (res.Contains("2160") || res.Contains("4k")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("2160p", "≈ 550 Mo");
            else if (res.Contains("1440")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("1440p", "≈ 250 Mo");
            else if (res.Contains("1080")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("1080p", "≈ 120 Mo");
            else if (res.Contains("720")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("720p", "≈ 65 Mo");
            else if (res.Contains("480")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("480p", "≈ 35 Mo");
            else if (res.Contains("360")) EstimatedSizeText = CurrentInspection.VideoEstimatedSizes.GetValueOrDefault("360p", "≈ 20 Mo");
            else EstimatedSizeText = "≈ 80 Mo";
        }
    }

    partial void OnIsAllAudioSelectedChanged(bool value)
    {
        foreach (var track in AudioTracks)
        {
            track.IsSelected = value;
        }
        UpdateAudioSummary();
    }

    partial void OnIsAllSubtitlesSelectedChanged(bool value)
    {
        foreach (var sub in SubtitleTracks)
        {
            sub.IsSelected = value;
        }
        UpdateSubtitlesSummary();
    }

    public void UpdateAudioSummary()
    {
        var selected = AudioTracks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SelectedAudioSummary = Loc["OptionOriginal"];
        }
        else if (selected.Count == AudioTracks.Count && AudioTracks.Count > 1)
        {
            SelectedAudioSummary = Loc.Format("OptionAllTracksCount", selected.Count);
        }
        else if (selected.Count == 1)
        {
            SelectedAudioSummary = selected[0].DisplayName;
        }
        else
        {
            SelectedAudioSummary = string.Join(", ", selected.Select(s => s.DisplayName));
        }
    }

    public void UpdateSubtitlesSummary()
    {
        var selected = SubtitleTracks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            SelectedSubtitlesSummary = Loc["OptionNone"];
        }
        else if (selected.Count == SubtitleTracks.Count && SubtitleTracks.Count > 1)
        {
            SelectedSubtitlesSummary = Loc.Format("OptionAllSubtitlesCount", selected.Count);
        }
        else if (selected.Count == 1)
        {
            SelectedSubtitlesSummary = selected[0].DisplayName;
        }
        else
        {
            SelectedSubtitlesSummary = string.Join(", ", selected.Select(s => s.DisplayName));
        }
    }

    private string _lastAutoDetectedClipboardUrl = string.Empty;

    public void CheckClipboardForUrl()
    {
        if (!_settingsService.Settings.ClipboardWatch) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(text) &&
                    (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                    !text.Contains('\n') && !text.Contains('\r'))
                {
                    if (text != _lastAutoDetectedClipboardUrl && text != InputUrl && !_settingsService.Settings.IgnoredDuplicateUrls.Contains(text))
                    {
                        _lastAutoDetectedClipboardUrl = text;
                        InputUrl = text;
                    }
                }
            }
        }
        catch
        {
            // Tolérance pour les accès concurrents au presse-papiers Windows
        }
    }

    [RelayCommand]
    private void ToggleAdvanced()
    {
        IsAdvancedOpen = !IsAdvancedOpen;
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        string url = InputUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        string qualityLabel = IsAudioMode ? SelectedAudioTier : SelectedResolution;

        // Contrôle des doublons approfondi par fichier, qualité et ID (Étape 3)
        string baseFolder = !string.IsNullOrWhiteSpace(_settingsService.Settings.DownloadFolder)
            ? _settingsService.Settings.DownloadFolder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string mediaTitle = !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : (CurrentInspection?.Title ?? url);

        var dupResult = await _duplicateService.CheckDuplicateAsync(url, mediaTitle, 0, baseFolder, qualityLabel, _databaseService);
        if (dupResult.Status != DuplicateStatus.None && !_settingsService.Settings.IgnoredDuplicateUrls.Contains(url))
        {
            string conflictMsg = string.Format(
                Loc["ConflictMessage"],
                Path.GetFileName(dupResult.ExistingFilePath),
                dupResult.ExistingQuality,
                qualityLabel);

            var choice = MessageBox.Show(
                conflictMsg,
                Loc["ConflictTitle"],
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.No)
            {
                return;
            }
        }
        else
        {
            var existing = Cards.FirstOrDefault(c => c.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !_settingsService.Settings.IgnoredDuplicateUrls.Contains(url))
            {
                var choice = MessageBox.Show(
                    $"Ce média a déjà été ajouté :\n« {existing.Title} »\n\nVoulez-vous continuer ?",
                    Loc["ConflictTitle"],
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.No)
                {
                    return;
                }
            }
        }

        // Détermination multi-pistes audio et sous-titres
        string selectedAudio;
        if (IsAllAudioSelected || (AudioTracks.Count > 0 && AudioTracks.All(t => t.IsSelected)))
        {
            var detectedAudioList = AudioTracks.Select(t => t.DisplayName).ToList();
            selectedAudio = detectedAudioList.Count > 0 ? string.Join(", ", detectedAudioList) : "ALL";
        }
        else
        {
            var selectedList = AudioTracks.Where(t => t.IsSelected).Select(t => t.DisplayName).ToList();
            selectedAudio = selectedList.Count > 0 ? string.Join(", ", selectedList) : SelectedAudioLanguage;
        }

        string selectedSubs;
        var selectedSubList = SubtitleTracks.Where(s => s.IsSelected).Select(s => s.DisplayName).ToList();
        if (IsAllSubtitlesSelected || (SubtitleTracks.Count > 0 && SubtitleTracks.All(s => s.IsSelected)))
        {
            var detectedList = SubtitleTracks.Select(s => s.DisplayName).ToList();
            selectedSubs = detectedList.Count > 0 ? string.Join(", ", detectedList) : "ALL";
        }
        else if (selectedSubList.Count > 0)
        {
            selectedSubs = string.Join(", ", selectedSubList);
        }
        else
        {
            selectedSubs = "Aucun";
        }

        // Cas 1 : Lien de Playlist détecté -> Écran de préparation (Étape 2)
        if (_playlistService.IsPlaylistUrl(url))
        {
            PlaylistInspectionResult? plResult = _cachedPlaylistResult;
            if (plResult == null || !plResult.SourceUrl.Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                IsInspecting = true;
                InspectionStatusText = LocalizationService.Instance["PlaylistInspecting"];
                plResult = await _playlistService.InspectPlaylistAsync(url);
                IsInspecting = false;
                InspectionStatusText = string.Empty;
            }

            if (plResult != null && plResult.Entries.Count > 0)
            {
                PlaylistPrepVm.LoadPlaylist(plResult, baseFolder, CustomTitle);
                PlaylistPrepVm.IsVideoMode = IsVideoMode;
                PlaylistPrepVm.IsAudioMode = IsAudioMode;
                PlaylistPrepVm.SelectedResolution = SelectedResolution;
                PlaylistPrepVm.SelectedAudioTier = SelectedAudioTier;
                PlaylistPrepVm.SelectedContainer = ItemContainer;
                PlaylistPrepVm.EmbedSubtitles = EmbedSubtitles;
                PlaylistPrepVm.RecalculateStats();

                IsPlaylistPrepOpen = true;
                return;
            }
        }

        // Cas 2 : Téléchargement direct d'une vidéo individuelle
        bool isLiveUrl = (CurrentInspection?.IsLive ?? false) ||
                         url.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("kick.com", StringComparison.OrdinalIgnoreCase);

        var item = new DownloadItem
        {
            Url = url,
            TargetUrl = string.Empty,
            Title = !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : (CurrentInspection?.Title ?? url),
            CustomTitle = CustomTitle,
            Quality = qualityLabel,
            AudioLanguage = selectedAudio,
            SubtitleTrack = selectedSubs,
            EmbedSubtitles = EmbedSubtitles,
            VideoResolution = SelectedResolution,
            VideoFps = ItemVideoFps,
            VideoCodec = ItemVideoCodec,
            VideoDynamicRange = ItemVideoDynamicRange,
            ContainerFormat = ItemContainer,
            SpeedLimit = ItemSpeedLimit,
            AudioCodec = ItemAudioCodec,
            AudioBitrate = ItemAudioBitrate,
            AudioSampleRate = ItemAudioSampleRate,
            AudioChannels = ItemAudioChannels,
            IsLive = isLiveUrl,
            LiveMaxDuration = _settingsService.Settings.LiveMaxDuration,
            Status = JobStatus.Queued,
            CreatedAt = DateTime.Now
        };

        await _databaseService.UpsertItemAsync(item);
        Logger.Log(Loc.Format("LogDownloadQueued", item.Title, item.Url));

        var cardVm = CreateCardViewModel(item);
        Cards.Insert(0, cardVm);
        _allFilteredCards.Insert(0, cardVm);
        RecalculateDateSeparators();

        InputUrl = string.Empty;
        CustomTitle = string.Empty;
        CurrentInspection = null;

        _downloadScheduler.Enqueue(
            item,
            onProgress: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var liveCard = Cards.FirstOrDefault(c => c.Id == updated.Id) ?? cardVm;
                    liveCard.UpdateFromModel(updated);
                });
            },
            onCompleted: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var liveCard = Cards.FirstOrDefault(c => c.Id == updated.Id) ?? cardVm;
                    liveCard.UpdateFromModel(updated);
                    if (updated.Status == JobStatus.Finished)
                    {
                        Logger.Log(Loc.Format("LogDownloadCompleted", updated.Title, updated.OutputPath), "SUCCESS");
                    }
                });
            }
        );
    }

    private async Task LaunchPreparedPlaylistDownloadAsync(PlaylistPreparationViewModel prepVm)
    {
        string url = prepVm.PlaylistUrl;
        string plTitle = !string.IsNullOrWhiteSpace(prepVm.CustomTitle) ? prepVm.CustomTitle : prepVm.PlaylistTitle;
        string safePlName = DownloadService.SanitizeFileName(plTitle);
        string targetFolder = prepVm.TargetFolder;
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            string baseFolder = !string.IsNullOrWhiteSpace(_settingsService.Settings.DownloadFolder)
                ? _settingsService.Settings.DownloadFolder
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            targetFolder = Path.Combine(baseFolder, safePlName);
        }

        string qualityLabel = prepVm.IsAudioMode ? prepVm.SelectedAudioTier : prepVm.SelectedResolution;

        var plJob = new PlaylistJob
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceUrl = url,
            Title = plTitle,
            CustomTitle = prepVm.CustomTitle,
            TargetFolder = targetFolder,
            TotalItems = prepVm.Entries.Count,
            Status = JobStatus.Queued,
            CreatedAt = DateTime.Now,
            NamingTemplate = prepVm.SelectedNamingTemplate,
            PreferredQuality = qualityLabel
        };

        await _databaseService.UpsertPlaylistAsync(plJob);

        var childItems = new List<DownloadItem>();
        foreach (var entryVm in prepVm.Entries)
        {
            JobStatus itemStatus;
            string statusDetail;

            if (!entryVm.IsAvailable)
            {
                itemStatus = JobStatus.Ignored;
                statusDetail = string.IsNullOrWhiteSpace(entryVm.UnavailableReason)
                    ? Loc["StatusIgnored"]
                    : entryVm.UnavailableReason;
            }
            else if (entryVm.IsSelected)
            {
                itemStatus = JobStatus.Queued;
                statusDetail = Loc["StatusQueued"];
            }
            else
            {
                itemStatus = JobStatus.Ignored;
                statusDetail = Loc["StatusNonSelected"];
            }

            var childItem = new DownloadItem
            {
                PlaylistId = plJob.Id,
                PlaylistIndex = entryVm.Index,
                PlaylistFolder = targetFolder,
                Url = entryVm.Url,
                Title = entryVm.Title,
                Quality = qualityLabel,
                AudioLanguage = "Originale",
                SubtitleTrack = "Aucun",
                EmbedSubtitles = prepVm.EmbedSubtitles,
                VideoResolution = prepVm.SelectedResolution,
                VideoFps = ItemVideoFps,
                VideoCodec = ItemVideoCodec,
                VideoDynamicRange = ItemVideoDynamicRange,
                ContainerFormat = prepVm.SelectedContainer,
                SpeedLimit = ItemSpeedLimit,
                AudioCodec = ItemAudioCodec,
                AudioBitrate = ItemAudioBitrate,
                AudioSampleRate = ItemAudioSampleRate,
                AudioChannels = ItemAudioChannels,
                NamingTemplate = prepVm.SelectedNamingTemplate,
                Status = itemStatus,
                StatusDetail = statusDetail,
                CreatedAt = DateTime.Now
            };

            await _databaseService.UpsertItemAsync(childItem);
            childItems.Add(childItem);
        }

        var parentCard = CreatePlaylistCardViewModel(plJob, childItems);
        Cards.Insert(0, parentCard);
        _allFilteredCards.Insert(0, parentCard);
        RecalculateDateSeparators();

        InputUrl = string.Empty;
        CustomTitle = string.Empty;
        CurrentInspection = null;
        _cachedPlaylistResult = null;

        var toEnqueue = childItems.Where(i => i.Status == JobStatus.Queued).ToList();
        _downloadScheduler.EnqueueRange(
            toEnqueue,
            onProgress: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = parentCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    parentCard.RecalculatePlaylistStats();
                });
            },
            onCompleted: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = parentCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    parentCard.RecalculatePlaylistStats();
                    CheckPlaylistCompletion(parentCard);
                });
            }
        );
    }

    public bool IsFilterAll => CurrentDateFilter == DateFilter.All && !IsFilterSpecificDate;
    public bool IsFilterToday => CurrentDateFilter == DateFilter.Today && !IsFilterSpecificDate;
    public bool IsFilterLast7Days => CurrentDateFilter == DateFilter.Last7Days && !IsFilterSpecificDate;
    public bool IsFilterThisMonth => CurrentDateFilter == DateFilter.ThisMonth && !IsFilterSpecificDate;

    [ObservableProperty]
    private DateTime? _selectedSpecificDate;

    [ObservableProperty]
    private bool _isFilterSpecificDate;

    [ObservableProperty]
    private bool _isCalendarOpen;

    [ObservableProperty]
    private bool _hasMoreItems;

    public string SpecificDateDisplayText => SelectedSpecificDate.HasValue ? SelectedSpecificDate.Value.ToString("dd/MM/yyyy") : string.Empty;

    private const int PageSize = 20;

    partial void OnSelectedSpecificDateChanged(DateTime? value)
    {
        if (value.HasValue)
        {
            IsFilterSpecificDate = true;
            IsCalendarOpen = false;
            NotifyFilterChanged();
            OnPropertyChanged(nameof(SpecificDateDisplayText));
            _ = LoadHistoryAsync(resetPagination: true);
        }
    }

    [RelayCommand]
    private async Task ClearSpecificDateAsync()
    {
        SelectedSpecificDate = null;
        IsFilterSpecificDate = false;
        IsCalendarOpen = false;
        NotifyFilterChanged();
        OnPropertyChanged(nameof(SpecificDateDisplayText));
        await LoadHistoryAsync(resetPagination: true);
    }

    private void NotifyFilterChanged()
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterToday));
        OnPropertyChanged(nameof(IsFilterLast7Days));
        OnPropertyChanged(nameof(IsFilterThisMonth));
        OnPropertyChanged(nameof(IsFilterSpecificDate));
    }

    [RelayCommand]
    private async Task SetDateFilterAsync(string filterStr)
    {
        if (Enum.TryParse<DateFilter>(filterStr, true, out var f))
        {
            SelectedSpecificDate = null;
            IsFilterSpecificDate = false;
            CurrentDateFilter = f;
            NotifyFilterChanged();
            OnPropertyChanged(nameof(SpecificDateDisplayText));
            await LoadHistoryAsync(resetPagination: true);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = LoadHistoryAsync(resetPagination: true);
    }

    private readonly List<JobCardViewModel> _allFilteredCards = new();

    public async Task LoadHistoryAsync(bool resetPagination = true)
    {
        if (resetPagination)
        {
            Cards.Clear();
            _allFilteredCards.Clear();

            DateTime? specDate = IsFilterSpecificDate ? SelectedSpecificDate : null;

            // 1. Playlists pour le filtre actif
            var playlists = await _databaseService.GetPlaylistsAsync(
                filter: CurrentDateFilter,
                searchQuery: SearchQuery,
                specificDate: specDate
            );

            foreach (var pl in playlists)
            {
                var plItems = await _databaseService.GetPlaylistItemsAsync(pl.Id);
                var reconciledItems = plItems.Select(it => _downloadScheduler.TryGetActive(it.Id, out var active) ? active! : it).ToList();
                _allFilteredCards.Add(CreatePlaylistCardViewModel(pl, reconciledItems));
            }

            // 2. Éléments indépendants pour le filtre actif (tous les éléments du filtre)
            var items = await _databaseService.GetItemsAsync(
                filter: CurrentDateFilter,
                searchQuery: SearchQuery,
                specificDate: specDate,
                limit: -1,
                offset: 0,
                standaloneOnly: true
            );

            foreach (var item in items)
            {
                var itemToUse = _downloadScheduler.TryGetActive(item.Id, out var active) ? active! : item;
                _allFilteredCards.Add(CreateCardViewModel(itemToUse));
            }

            // Tri combiné strict par date décroissante
            var sorted = _allFilteredCards.OrderByDescending(c => c.CreatedAt).ToList();
            _allFilteredCards.Clear();
            _allFilteredCards.AddRange(sorted);

            // Affichage initial : STRICTEMENT au maximum 20 cartes
            var initialBatch = _allFilteredCards.Take(PageSize).ToList();
            foreach (var card in initialBatch)
            {
                Cards.Add(card);
            }
        }
        else
        {
            // Clic sur "Montrer plus" : ajout des 20 cartes suivantes
            int current = Cards.Count;
            var nextBatch = _allFilteredCards.Skip(current).Take(PageSize).ToList();
            foreach (var card in nextBatch)
            {
                Cards.Add(card);
            }
        }

        HasMoreItems = Cards.Count < _allFilteredCards.Count;
        RecalculateDateSeparators();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        await LoadHistoryAsync(resetPagination: false);
    }

    private void RecalculateDateSeparators()
    {
        HasNoCards = Cards.Count == 0;
        DateTime? lastDate = null;
        foreach (var card in Cards)
        {
            var d = card.CreatedAt.Date;
            if (lastDate == null || d != lastDate.Value)
            {
                card.ShowDateSeparator = true;
                if (d == DateTime.Today)
                {
                    card.DateHeader = Loc["Today"];
                }
                else if (d == DateTime.Today.AddDays(-1))
                {
                    card.DateHeader = Loc["Yesterday"];
                }
                else
                {
                    card.DateHeader = d.ToString("dd MMMM yyyy");
                }
                lastDate = d;
            }
            else
            {
                card.ShowDateSeparator = false;
            }
        }
    }

    [RelayCommand]
    private async Task ClearListAsync()
    {
        await _databaseService.ClearFinishedItemsAsync();
        Cards.Clear();
        _allFilteredCards.Clear();
        HasMoreItems = false;
        HasNoCards = true;
        Logger.Log(Loc["LogDownloadListCleared"]);
    }

    private JobCardViewModel CreatePlaylistCardViewModel(PlaylistJob playlist, List<DownloadItem> childItems)
    {
        var pseudoItem = new DownloadItem
        {
            Id = playlist.Id,
            Url = playlist.SourceUrl,
            Title = !string.IsNullOrWhiteSpace(playlist.CustomTitle) ? playlist.CustomTitle : playlist.Title,
            CustomTitle = playlist.CustomTitle,
            Status = playlist.Status,
            CreatedAt = playlist.CreatedAt,
            FinishedAt = playlist.FinishedAt
        };

        JobCardViewModel? playlistCard = null;

        playlistCard = new JobCardViewModel(
            pseudoItem,
            onRetry: async (it) =>
            {
                await RetryFailedPlaylistAsync(playlistCard);
            },
            onDelete: async (it, deleteFiles) =>
            {
                Cards.Remove(playlistCard!);
                RecalculateDateSeparators();
                HasNoCards = Cards.Count == 0;
                _downloadScheduler.CancelPlaylist(playlist.Id);
                foreach (var ci in childItems)
                {
                    if (deleteFiles) _downloadService.DeleteAllAssociatedFiles(ci);
                    await _databaseService.DeleteItemAsync(ci.Id);
                }
                await _databaseService.DeletePlaylistAsync(playlist.Id);
                Logger.Log(Loc.Format("LogPlaylistRemoved", playlist.Title));
            },
            onOpenFolder: (it) =>
            {
                string folder = !string.IsNullOrWhiteSpace(playlist.TargetFolder) ? playlist.TargetFolder : _settingsService.Settings.DownloadFolder;
                if (Directory.Exists(folder)) Process.Start("explorer.exe", folder);
            },
            onOpenFile: (it) =>
            {
                string folder = !string.IsNullOrWhiteSpace(playlist.TargetFolder) ? playlist.TargetFolder : _settingsService.Settings.DownloadFolder;
                if (Directory.Exists(folder)) Process.Start("explorer.exe", folder);
            },
            onPausePlaylist: async (pl) =>
            {
                _downloadScheduler.PausePlaylist(pl.Id);
                pl.Status = JobStatus.Paused;
                await _databaseService.UpsertPlaylistAsync(pl);
                playlistCard?.RecalculatePlaylistStats();
            },
            onResumePlaylist: async (pl) =>
            {
                await ResumePlaylistAsync(playlistCard);
            },
            onCancelPlaylist: async (pl) =>
            {
                _downloadScheduler.CancelPlaylist(pl.Id);
                pl.Status = JobStatus.Cancelled;
                await _databaseService.UpsertPlaylistAsync(pl);
                playlistCard?.RecalculatePlaylistStats();
            },
            onRetryFailedPlaylist: async (pl) =>
            {
                await RetryFailedPlaylistAsync(playlistCard);
            }
        )
        {
            IsPlaylist = true,
            Playlist = playlist
        };

        foreach (var childItem in childItems)
        {
            var childVm = CreateCardViewModel(childItem, parentPlaylistCard: playlistCard);
            playlistCard.ChildCards.Add(childVm);
        }

        playlistCard.RecalculatePlaylistStats();
        return playlistCard;
    }

    private async Task ResumePlaylistAsync(JobCardViewModel? playlistCard)
    {
        if (playlistCard?.Playlist == null) return;
        var pl = playlistCard.Playlist;
        var toResume = playlistCard.ChildCards
            .Where(c => c.Status == JobStatus.Paused)
            .Select(c => c.Item)
            .ToList();

        if (toResume.Count == 0) return;

        foreach (var item in toResume)
        {
            _downloadService.ResumeDownload(item);
            item.Status = JobStatus.Queued;
            item.StatusDetail = Loc["StatusQueued"];
            var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == item.Id);
            childVm?.UpdateFromModel(item);
        }

        playlistCard.RecalculatePlaylistStats();
        pl.Status = JobStatus.Downloading;
        await _databaseService.UpsertPlaylistAsync(pl);

        _downloadScheduler.EnqueueRange(
            toResume,
            onProgress: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    playlistCard.RecalculatePlaylistStats();
                });
            },
            onCompleted: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    playlistCard.RecalculatePlaylistStats();
                    CheckPlaylistCompletion(playlistCard);
                });
            }
        );
    }

    private async Task RetryFailedPlaylistAsync(JobCardViewModel? playlistCard)
    {
        if (playlistCard?.Playlist == null) return;
        var pl = playlistCard.Playlist;
        var toRetry = playlistCard.ChildCards
            .Where(c => c.Status == JobStatus.Error)
            .Select(c => c.Item)
            .ToList();

        if (toRetry.Count == 0) return;

        foreach (var item in toRetry)
        {
            _downloadService.ResumeDownload(item);
            item.Status = JobStatus.Queued;
            item.ErrorMessage = string.Empty;
            item.LastError = string.Empty;
            item.StatusDetail = Loc["StatusQueued"];
            var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == item.Id);
            childVm?.UpdateFromModel(item);
        }

        playlistCard.RecalculatePlaylistStats();
        pl.Status = JobStatus.Downloading;
        await _databaseService.UpsertPlaylistAsync(pl);

        _downloadScheduler.EnqueueRange(
            toRetry,
            onProgress: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    playlistCard.RecalculatePlaylistStats();
                });
            },
            onCompleted: (updated) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var childVm = playlistCard.ChildCards.FirstOrDefault(c => c.Id == updated.Id);
                    childVm?.UpdateFromModel(updated);
                    playlistCard.RecalculatePlaylistStats();
                    CheckPlaylistCompletion(playlistCard);
                });
            }
        );
    }

    private void CheckPlaylistCompletion(JobCardViewModel playlistCard)
    {
        if (playlistCard.Playlist == null) return;
        int total = playlistCard.TotalVideosCount;
        int finished = playlistCard.FinishedCount;
        int ignored = playlistCard.IgnoredCount;
        int errors = playlistCard.ErrorCount;

        if (total > 0 && (finished + ignored + errors) >= total)
        {
            if (errors == 0)
            {
                playlistCard.Playlist.Status = JobStatus.Finished;
                playlistCard.Playlist.FinishedAt = DateTime.Now;
            }
            else
            {
                playlistCard.Playlist.Status = JobStatus.Error;
            }

            _ = _databaseService.UpsertPlaylistAsync(playlistCard.Playlist);
            string report = string.Format(Loc["PlaylistCompletionReport"], finished, ignored, errors);
            Logger.Log(Loc.Format("LogPlaylistReport", playlistCard.Playlist.Title, report), errors == 0 ? "SUCCESS" : "WARNING");
        }
    }

    private JobCardViewModel CreateCardViewModel(DownloadItem item, JobCardViewModel? parentPlaylistCard = null)
    {
        JobCardViewModel? cardVm = null;
        cardVm = new JobCardViewModel(
            item,
            onRetry: async (it) =>
            {
                var vm = parentPlaylistCard?.ChildCards.FirstOrDefault(c => c.Id == it.Id) 
                         ?? Cards.FirstOrDefault(c => c.Id == it.Id) ?? cardVm;
                if (vm != null)
                {
                    _downloadService.ResumeDownload(it);
                    bool wasPaused = it.Status == JobStatus.Paused;
                    it.Status = JobStatus.Queued;
                    if (!wasPaused)
                    {
                        it.Progress = 0;
                    }
                    it.ErrorMessage = string.Empty;
                    it.LastError = string.Empty;
                    it.StatusDetail = Loc["StatusQueued"];
                    vm.UpdateFromModel(it);
                    parentPlaylistCard?.RecalculatePlaylistStats();
                    await _databaseService.UpsertItemAsync(it);

                    _downloadScheduler.Enqueue(
                        it,
                        onProgress: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                vm.UpdateFromModel(updated);
                                parentPlaylistCard?.RecalculatePlaylistStats();
                            });
                        },
                        onCompleted: (updated) =>
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                vm.UpdateFromModel(updated);
                                parentPlaylistCard?.RecalculatePlaylistStats();
                                if (parentPlaylistCard != null)
                                {
                                    CheckPlaylistCompletion(parentPlaylistCard);
                                }
                            });
                        }
                    );
                }
            },
            onDelete: async (it, deleteFiles) =>
            {
                if (parentPlaylistCard != null)
                {
                    var childVm = parentPlaylistCard.ChildCards.FirstOrDefault(c => c.Id == it.Id);
                    if (childVm != null)
                    {
                        parentPlaylistCard.ChildCards.Remove(childVm);
                        parentPlaylistCard.RecalculatePlaylistStats();
                    }
                }
                else
                {
                    var vm = Cards.FirstOrDefault(c => c.Id == it.Id);
                    if (vm != null)
                    {
                        Cards.Remove(vm);
                        RecalculateDateSeparators();
                        HasNoCards = Cards.Count == 0;
                    }
                }

                _downloadScheduler.Cancel(it.Id);
                if (deleteFiles)
                {
                    _downloadService.DeleteAllAssociatedFiles(it);
                }
                await _databaseService.DeleteItemAsync(it.Id);
                Logger.Log(Loc.Format("LogCardRemoved", it.Title));
            },
            onOpenFolder: (it) =>
            {
                string path = it.OutputPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    string folder = !string.IsNullOrWhiteSpace(it.PlaylistFolder) ? it.PlaylistFolder : _settingsService.Settings.DownloadFolder;
                    path = DownloadService.ResolveActualFilePath(path, folder, it.CustomTitle, it.Title);
                }

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else
                {
                    string folder = !string.IsNullOrWhiteSpace(it.PlaylistFolder) ? it.PlaylistFolder : _settingsService.Settings.DownloadFolder;
                    if (Directory.Exists(folder)) Process.Start("explorer.exe", folder);
                }
            },
            onOpenFile: (it) =>
            {
                string path = it.OutputPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    string folder = !string.IsNullOrWhiteSpace(it.PlaylistFolder) ? it.PlaylistFolder : _settingsService.Settings.DownloadFolder;
                    path = DownloadService.ResolveActualFilePath(path, folder, it.CustomTitle, it.Title);
                }

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    it.OutputPath = path;
                    _ = _databaseService.UpsertItemAsync(it);
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else
                {
                    string folder = !string.IsNullOrWhiteSpace(it.PlaylistFolder) ? it.PlaylistFolder : _settingsService.Settings.DownloadFolder;
                    if (Directory.Exists(folder))
                    {
                        Process.Start("explorer.exe", folder);
                    }
                }
            },
            onPause: async (it) =>
            {
                _downloadScheduler.Pause(it.Id);
                it.Status = JobStatus.Paused;
                it.StatusDetail = Loc["StatusPaused"];
                it.SpeedStr = string.Empty;
                it.EtaStr = string.Empty;
                var vm = parentPlaylistCard?.ChildCards.FirstOrDefault(c => c.Id == it.Id) 
                         ?? Cards.FirstOrDefault(c => c.Id == it.Id) ?? cardVm;
                vm?.UpdateFromModel(it);
                parentPlaylistCard?.RecalculatePlaylistStats();
                await _databaseService.UpsertItemAsync(it);
                Logger.Log(Loc.Format("LogDownloadPaused", it.Title));
            },
            onCancel: async (it) =>
            {
                _downloadScheduler.Cancel(it.Id);
                if (parentPlaylistCard != null)
                {
                    var childVm = parentPlaylistCard.ChildCards.FirstOrDefault(c => c.Id == it.Id);
                    if (childVm != null)
                    {
                        childVm.Status = JobStatus.Cancelled;
                        childVm.StatusDetail = Loc["StatusCancelled"];
                        parentPlaylistCard.RecalculatePlaylistStats();
                    }
                }
                else
                {
                    var vm = Cards.FirstOrDefault(c => c.Id == it.Id);
                    if (vm != null)
                    {
                        Cards.Remove(vm);
                        RecalculateDateSeparators();
                        HasNoCards = Cards.Count == 0;
                    }
                }
                _downloadService.CleanupIntermediateFilesOnly(it);
                await _databaseService.DeleteItemAsync(it.Id);
                Logger.Log(Loc.Format("LogDownloadCancelled", it.Title));
            },
            onKeepClip: async (it) =>
            {
                var vm = Cards.FirstOrDefault(c => c.Id == it.Id) ?? cardVm;
                if (vm != null)
                {
                    await _downloadService.FinalizeInterruptedLiveAsync(it, updated =>
                    {
                        Application.Current.Dispatcher.Invoke(() => vm.UpdateFromModel(updated));
                    });
                    await _databaseService.UpsertItemAsync(it);
                    Logger.Log(Loc.Format("LogLiveFinalized", it.Title, it.OutputPath), "SUCCESS");
                }
            },
            onDeleteLive: async (it) =>
            {
                var vm = Cards.FirstOrDefault(c => c.Id == it.Id) ?? cardVm;
                if (vm != null)
                {
                    vm.Cts?.Cancel();
                    Cards.Remove(vm);
                    RecalculateDateSeparators();
                }
                _downloadService.DiscardInterruptedLive(it);
                await _databaseService.DeleteItemAsync(it.Id);
                Logger.Log(Loc.Format("LogLiveDeleted", it.Title));
            },
            onManualExtract: async (it) =>
            {
                var vm = parentPlaylistCard?.ChildCards.FirstOrDefault(c => c.Id == it.Id) 
                         ?? Cards.FirstOrDefault(c => c.Id == it.Id) ?? cardVm;
                if (vm == null) return;

                var loc = LocalizationService.Instance;
                it.Status = JobStatus.Downloading;
                it.StatusDetail = loc["SniffPromptPlay"];
                it.Progress = 0;
                it.ErrorMessage = string.Empty;
                vm.UpdateFromModel(it);

                var sniffer = new StreamSnifferService();
                var browser = _settingsService.Settings.ExtractionBrowser;
                Logger.Log(Loc.Format("LogSniffStarted", browser, it.Title, it.Url));

                var sniffRes = await sniffer.SniffAsync(it.Url, headless: false, browser: browser, timeoutSeconds: 600);
                if (sniffRes.Success && !string.IsNullOrWhiteSpace(sniffRes.StreamUrl))
                {
                    Logger.Log(Loc.Format("LogSniffSuccess", sniffRes.StreamUrl), "SUCCESS");
                    it.TargetUrl = sniffRes.StreamUrl;
                    it.Referer = sniffRes.Referer;
                    if (string.IsNullOrWhiteSpace(it.Title) && !string.IsNullOrWhiteSpace(sniffRes.Title))
                    {
                        it.Title = sniffRes.Title;
                    }
                    it.StatusDetail = loc["SniffManualSuccess"];
                    vm.UpdateFromModel(it);

                    // Feedback positif sous forme de bannière de succès
                    SuccessNotificationTitle = string.Format(loc["SniffSuccessBanner"], !string.IsNullOrWhiteSpace(it.Title) ? it.Title : it.Url);
                    SuccessNotificationMessage = loc["SniffSuccessBannerDesc"];
                    IsSuccessNotificationVisible = true;

                    // Masquage automatique de la bulle après 6 secondes
                    _ = Task.Delay(6000).ContinueWith(_ =>
                    {
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            IsSuccessNotificationVisible = false;
                        });
                    });

                    _downloadScheduler.Enqueue(
                        it,
                        onProgress: (updated) => Application.Current.Dispatcher.Invoke(() => vm.UpdateFromModel(updated)),
                        onCompleted: (updated) => Application.Current.Dispatcher.Invoke(() => vm.UpdateFromModel(updated))
                    );
                }
                else
                {
                    it.Status = JobStatus.Error;
                    it.ErrorMessage = !string.IsNullOrWhiteSpace(sniffRes.Error) ? sniffRes.Error : loc["SniffManualFailed"];
                    Logger.Log(Loc.Format("LogSniffFailed", it.Title, it.ErrorMessage), "ERROR");
                    vm.UpdateFromModel(it);
                    await _databaseService.UpsertItemAsync(it);
                }
            });

        return cardVm;
    }
}
