using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDownloader.Core.Models;
using UberDownloader.Core.Services;

namespace UberDownloader.Wpf.ViewModels;

public class LogEntry
{
    public string Text { get; set; } = string.Empty;
    public bool IsLast { get; set; }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DownloadScheduler _downloadScheduler;

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty]
    private string _downloadFolder = string.Empty;

    [ObservableProperty]
    private string _defaultQuality = "1080p Full HD";

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private string _selectedTheme = "Nuit";

    public ObservableCollection<string> AvailableThemes { get; } = new();

    public event Action<string>? LogsRefreshed;

    private bool _isRefreshingOptions;

    private string GetLocalizedTheme(string themeCanonical)
    {
        return themeCanonical switch
        {
            "Light" or "Jour" or "Día" => Loc["ThemeLight"],
            "System" or "Système" or "Sistema" => Loc["ThemeSystem"],
            _ => Loc["ThemeDark"]
        };
    }

    private string GetCanonicalTheme(string localizedTheme)
    {
        if (string.Equals(localizedTheme, Loc["ThemeLight"], StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "Light", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "Jour", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "Día", StringComparison.OrdinalIgnoreCase))
            return "Light";

        if (string.Equals(localizedTheme, Loc["ThemeSystem"], StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "System", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "Système", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localizedTheme, "Sistema", StringComparison.OrdinalIgnoreCase))
            return "System";

        return "Dark";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || _isRefreshingOptions) return;
        string canonical = GetCanonicalTheme(value);
        _theme = canonical;
        UberDownloader.Wpf.Services.ThemeService.ApplyTheme(canonical);
    }

    [ObservableProperty]
    private string _language = "FR";

    [ObservableProperty]
    private int _maxConcurrentDownloads = 2;

    [ObservableProperty]
    private bool _isMaxConcurrentUnlimited = false;

    [ObservableProperty]
    private bool _autoResumeOnStartup = true;

    [ObservableProperty]
    private int _maxRetries = 3;

    [ObservableProperty]
    private bool _isMaxRetriesUnlimited = false;

    [ObservableProperty]
    private string _speedLimit = "Illimité";

    [ObservableProperty]
    private string _globalHotkey = "Ctrl+Shift+O";

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private string _hotkeySlot1 = string.Empty;

    [ObservableProperty]
    private string _hotkeySlot2 = string.Empty;

    [ObservableProperty]
    private string _hotkeySlot3 = string.Empty;

    public bool CanConfirmHotkey => !string.IsNullOrWhiteSpace(HotkeySlot1) && 
                                    !string.IsNullOrWhiteSpace(HotkeySlot2) && 
                                    !string.IsNullOrWhiteSpace(HotkeySlot3);

    [RelayCommand]
    private void StartHotkeySetup()
    {
        IsCapturingHotkey = true;
        HotkeySlot1 = string.Empty;
        HotkeySlot2 = string.Empty;
        HotkeySlot3 = string.Empty;
        OnPropertyChanged(nameof(CanConfirmHotkey));
    }

    [RelayCommand]
    private void ResetHotkeySlots()
    {
        HotkeySlot1 = string.Empty;
        HotkeySlot2 = string.Empty;
        HotkeySlot3 = string.Empty;
        OnPropertyChanged(nameof(CanConfirmHotkey));
    }

    [RelayCommand]
    private void CancelHotkeySetup()
    {
        IsCapturingHotkey = false;
        HotkeySlot1 = string.Empty;
        HotkeySlot2 = string.Empty;
        HotkeySlot3 = string.Empty;
        OnPropertyChanged(nameof(CanConfirmHotkey));
    }

    [RelayCommand]
    private void ConfirmHotkeySetup()
    {
        var keys = new List<string>();
        if (!string.IsNullOrWhiteSpace(HotkeySlot1)) keys.Add(HotkeySlot1);
        if (!string.IsNullOrWhiteSpace(HotkeySlot2)) keys.Add(HotkeySlot2);
        if (!string.IsNullOrWhiteSpace(HotkeySlot3)) keys.Add(HotkeySlot3);

        if (keys.Count > 0)
        {
            string candidate = string.Join("+", keys);
            if (HotkeyHelper.TryParse(candidate, out _, out _))
            {
                GlobalHotkey = candidate;
                IsCapturingHotkey = false;
                RecalculatePendingChanges();
            }
        }
    }

    public void RegisterKeyStroke(string keyName)
    {
        if (!IsCapturingHotkey) return;

        if (string.IsNullOrEmpty(HotkeySlot1))
        {
            HotkeySlot1 = keyName;
        }
        else if (string.IsNullOrEmpty(HotkeySlot2))
        {
            if (!string.Equals(HotkeySlot1, keyName, StringComparison.OrdinalIgnoreCase))
            {
                HotkeySlot2 = keyName;
            }
        }
        else if (string.IsNullOrEmpty(HotkeySlot3))
        {
            if (!string.Equals(HotkeySlot1, keyName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(HotkeySlot2, keyName, StringComparison.OrdinalIgnoreCase))
            {
                HotkeySlot3 = keyName;
            }
        }
        OnPropertyChanged(nameof(CanConfirmHotkey));
    }

    public List<string> AvailableHotkeys { get; } = new()
    {
        "Ctrl+Shift+O",
        "Ctrl+Alt+D",
        "Ctrl+Shift+D",
        "Ctrl+Shift+U",
        "Alt+D",
        "F9",
        "F10",
        "F11"
    };

    [ObservableProperty]
    private string _playlistNamingTemplate = "{index} - {title}";

    public List<string> AvailableNamingTemplates { get; } = new()
    {
        "{index} - {title}",
        "{title}",
        "{index}. {title}",
        "{playlist} - {index} - {title}",
        "{index} - {id} - {title}"
    };

    // Directs / Lives
    [ObservableProperty]
    private string _liveEngine = "Streamlink (recommandé)";

    [ObservableProperty]
    private string _liveRecordMode = "Enregistrer les directs depuis le clic";

    [ObservableProperty]
    private string _liveMaxDuration = "Illimité";

    [ObservableProperty]
    private bool _isLiveDurationUnlimited = true;

    [ObservableProperty]
    private string _liveMaxDurationCustom = "60 min";

    [ObservableProperty]
    private string _liveDurationValidationMessage = string.Empty;

    [ObservableProperty]
    private string _liveInterruptionAction = "Demander à chaque fois";

    // Paramètres avancés Vidéo
    [ObservableProperty]
    private string _advancedVideoResolution = "Automatique";

    [ObservableProperty]
    private string _advancedVideoFps = "Conserver celle de la source";

    [ObservableProperty]
    private string _advancedVideoCodec = "Automatique";

    [ObservableProperty]
    private string _advancedVideoDynamicRange = "Automatique";

    [ObservableProperty]
    private string _advancedVideoBitrate = "Automatique";

    // Paramètres avancés Audio
    [ObservableProperty]
    private string _advancedAudioCodec = "Automatique";

    [ObservableProperty]
    private string _advancedAudioBitrate = "Automatique";

    [ObservableProperty]
    private string _advancedAudioSampleRate = "Conserver celle de la source";

    [ObservableProperty]
    private string _advancedAudioBitDepth = "Conserver celle de la source";

    [ObservableProperty]
    private string _advancedAudioChannels = "Automatique";

    [ObservableProperty]
    private bool _autoSniff = true;

    [ObservableProperty]
    private string _extractionBrowser = "Chromium";

    public IReadOnlyList<string> AvailableExtractionBrowsers { get; } = new[]
    {
        "Chromium",
        "Google Chrome",
        "Microsoft Edge",
        "Brave",
        "Opera GX",
        "Opera",
        "Opera Developer",
        "Opera Beta",
        "Tor Browser",
        "Firefox"
    };

    [ObservableProperty]
    private bool _clipboardWatch = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _embedThumbnail = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Bulle de confirmation de sauvegarde
    [ObservableProperty]
    private bool _isSuccessBubbleVisible;

    [ObservableProperty]
    private string _successBubbleTitle = string.Empty;

    [ObservableProperty]
    private string _successBubbleSubtitle = string.Empty;

    // Barre transversale comptant le nombre de changements avant enregistrement
    [ObservableProperty]
    private int _pendingChangesCount;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string _pendingChangesBannerText = string.Empty;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public List<string> AvailableLanguages { get; } = new() { "FR", "EN", "ES" };

    public ObservableCollection<string> AvailableSpeedLimits { get; } = new();
    public ObservableCollection<string> AvailableLiveEngines { get; } = new();
    public ObservableCollection<string> AvailableLiveRecordModes { get; } = new();
    public ObservableCollection<string> AvailableLiveInterruptionActions { get; } = new();

    // Options avancées Vidéo
    public ObservableCollection<string> AvailableVideoResolutions { get; } = new();
    public ObservableCollection<string> AvailableVideoFpsList { get; } = new();
    public ObservableCollection<string> AvailableVideoCodecs { get; } = new();
    public ObservableCollection<string> AvailableDynamicRanges { get; } = new();
    public ObservableCollection<string> AvailableVideoBitrates { get; } = new();

    // Options avancées Audio
    public ObservableCollection<string> AvailableAudioCodecs { get; } = new();
    public ObservableCollection<string> AvailableAudioBitrates { get; } = new();
    public ObservableCollection<string> AvailableAudioSampleRates { get; } = new();
    public ObservableCollection<string> AvailableAudioBitDepths { get; } = new();
    public ObservableCollection<string> AvailableAudioChannels { get; } = new();

    public void RefreshLocalizedOptions()
    {
        bool wasRefreshing = _isRefreshingOptions;
        _isRefreshingOptions = true;
        try
        {
            string targetTheme = GetLocalizedTheme(Theme);
            string prevSpeed = SpeedLimit;
            string prevLiveEngine = LiveEngine;
            string prevLiveRecordMode = LiveRecordMode;
            string prevLiveAction = LiveInterruptionAction;
            string prevVideoRes = AdvancedVideoResolution;
            string prevVideoFps = AdvancedVideoFps;
            string prevVideoCodec = AdvancedVideoCodec;
            string prevVideoRange = AdvancedVideoDynamicRange;
            string prevVideoBitrate = AdvancedVideoBitrate;
            string prevAudioCodec = AdvancedAudioCodec;
            string prevAudioBitrate = AdvancedAudioBitrate;
            string prevAudioSample = AdvancedAudioSampleRate;
            string prevAudioDepth = AdvancedAudioBitDepth;
            string prevAudioChannels = AdvancedAudioChannels;

            ReplaceItems(AvailableThemes, Loc["ThemeDark"], Loc["ThemeLight"], Loc["ThemeSystem"]);

            ReplaceItems(AvailableSpeedLimits,
                "500 Ko/s", "1 Mo/s", "5 Mo/s", "10 Mo/s", "20 Mo/s", "50 Mo/s", "100 Mo/s", Loc["OptionUnlimited"]);

            ReplaceItems(AvailableLiveEngines,
                Loc["LiveEngineStreamlink"], "yt-dlp");

            ReplaceItems(AvailableLiveRecordModes,
                Loc["LiveRecordFromStart"], Loc["LiveRecordFromClick"]);

            ReplaceItems(AvailableLiveInterruptionActions,
                Loc["LiveActionAsk"], Loc["LiveActionKeep"], Loc["LiveActionRetry"]);

            ReplaceItems(AvailableVideoResolutions,
                Loc["OptionAutomatic"], "144p", "240p", "360p", "480p", "720p", "1080p", "1440p", "2160p (4K)", "4320p (8K)");

            ReplaceItems(AvailableVideoFpsList,
                Loc["OptionKeepSource"], "24", "25", "30", "50", "60", "120");

            ReplaceItems(AvailableVideoCodecs,
                Loc["OptionAutomatic"], "H.264/AVC", "H.265/HEVC", "VP9", "AV1");

            ReplaceItems(AvailableDynamicRanges,
                Loc["OptionAutomatic"], "SDR", "HDR10", "Dolby Vision");

            ReplaceItems(AvailableVideoBitrates,
                Loc["OptionAutomatic"], Loc["OptionSourceStream"], "1000 kb/s", "2500 kb/s", "5000 kb/s", "8000 kb/s", "15000 kb/s");

            ReplaceItems(AvailableAudioCodecs,
                Loc["OptionAutomatic"], "AAC", "MP3", "Opus", "FLAC", "ALAC");

            ReplaceItems(AvailableAudioBitrates,
                Loc["OptionAutomatic"], "96 kb/s", "128 kb/s", "192 kb/s", "256 kb/s", "320 kb/s");

            ReplaceItems(AvailableAudioSampleRates,
                Loc["OptionKeepSource"], "44,1 kHz", "48 kHz", "88,2 kHz", "96 kHz", "176,4 kHz", "192 kHz");

            ReplaceItems(AvailableAudioBitDepths,
                Loc["OptionKeepSource"], "16 bits", "24 bits");

            ReplaceItems(AvailableAudioChannels,
                Loc["OptionAutomatic"], Loc["ChannelMono"], Loc["ChannelStereo"], Loc["ChannelMultichannel"]);

            SelectedTheme = FindMatchingOption(AvailableThemes, targetTheme, Loc["ThemeDark"]);
            SpeedLimit = FindMatchingOption(AvailableSpeedLimits, prevSpeed, Loc["OptionUnlimited"]);
            LiveEngine = FindMatchingOption(AvailableLiveEngines, prevLiveEngine, Loc["LiveEngineStreamlink"]);
            LiveRecordMode = FindMatchingOption(AvailableLiveRecordModes, prevLiveRecordMode, Loc["LiveRecordFromClick"]);
            LiveInterruptionAction = FindMatchingOption(AvailableLiveInterruptionActions, prevLiveAction, Loc["LiveActionAsk"]);
            AdvancedVideoResolution = FindMatchingOption(AvailableVideoResolutions, prevVideoRes, Loc["OptionAutomatic"]);
            AdvancedVideoFps = FindMatchingOption(AvailableVideoFpsList, prevVideoFps, Loc["OptionKeepSource"]);
            AdvancedVideoCodec = FindMatchingOption(AvailableVideoCodecs, prevVideoCodec, Loc["OptionAutomatic"]);
            AdvancedVideoDynamicRange = FindMatchingOption(AvailableDynamicRanges, prevVideoRange, Loc["OptionAutomatic"]);
            AdvancedVideoBitrate = FindMatchingOption(AvailableVideoBitrates, prevVideoBitrate, Loc["OptionAutomatic"]);
            AdvancedAudioCodec = FindMatchingOption(AvailableAudioCodecs, prevAudioCodec, Loc["OptionAutomatic"]);
            AdvancedAudioBitrate = FindMatchingOption(AvailableAudioBitrates, prevAudioBitrate, Loc["OptionAutomatic"]);
            AdvancedAudioSampleRate = FindMatchingOption(AvailableAudioSampleRates, prevAudioSample, Loc["OptionKeepSource"]);
            AdvancedAudioBitDepth = FindMatchingOption(AvailableAudioBitDepths, prevAudioDepth, Loc["OptionKeepSource"]);
            AdvancedAudioChannels = FindMatchingOption(AvailableAudioChannels, prevAudioChannels, Loc["OptionAutomatic"]);

            if (IsLiveDurationUnlimited)
            {
                LiveMaxDuration = Loc["OptionUnlimited"];
            }
        }
        finally
        {
            _isRefreshingOptions = wasRefreshing;
        }
    }

    private static string FindMatchingOption(IEnumerable<string> options, string? current, string fallback)
    {
        var list = options as IList<string> ?? options.ToList();
        if (list.Count == 0) return fallback;

        if (!string.IsNullOrWhiteSpace(current))
        {
            var exact = list.FirstOrDefault(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var translated = LocalizationService.Instance.TranslateOption(current);
            var matchTranslated = list.FirstOrDefault(x => string.Equals(x, translated, StringComparison.OrdinalIgnoreCase));
            if (matchTranslated != null) return matchTranslated;
        }

        var matchFallback = list.FirstOrDefault(x => string.Equals(x, fallback, StringComparison.OrdinalIgnoreCase));
        return matchFallback ?? list[0];
    }

    private static void ReplaceItems(ObservableCollection<string> collection, params string[] items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private sealed record SettingsSnapshot(
        string DownloadFolder,
        string SelectedTheme,
        string Language,
        int MaxConcurrentDownloads,
        bool IsMaxConcurrentUnlimited,
        string SpeedLimit,
        string PlaylistNamingTemplate,
        string LiveEngine,
        string LiveRecordMode,
        bool IsLiveDurationUnlimited,
        string LiveMaxDurationCustom,
        string LiveInterruptionAction,
        string AdvancedVideoResolution,
        string AdvancedVideoFps,
        string AdvancedVideoCodec,
        string AdvancedVideoDynamicRange,
        string AdvancedVideoBitrate,
        string AdvancedAudioCodec,
        string AdvancedAudioBitrate,
        string AdvancedAudioSampleRate,
        string AdvancedAudioBitDepth,
        string AdvancedAudioChannels,
        bool AutoSniff,
        string ExtractionBrowser,
        bool ClipboardWatch,
        bool NotificationsEnabled,
        bool EmbedThumbnail,
        bool AutoResumeOnStartup,
        int MaxRetries,
        bool IsMaxRetriesUnlimited,
        string GlobalHotkey
    );

    private SettingsSnapshot? _initialSnapshot;

    public Action? RequestClose { get; set; }

    public SettingsViewModel(SettingsService settingsService, DownloadScheduler downloadScheduler)
    {
        _settingsService = settingsService;
        _downloadScheduler = downloadScheduler;
        LocalizationService.Instance.LanguageChanged += () =>
        {
            RefreshLocalizedOptions();
            RecalculatePendingChanges();
        };
        LoadSettings();
    }

    public void LoadSettings()
    {
        _isRefreshingOptions = true;
        try
        {
            var s = _settingsService.Settings;
            DownloadFolder = string.IsNullOrWhiteSpace(s.DownloadFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : s.DownloadFolder;
            DefaultQuality = s.DefaultQuality;
            Theme = string.IsNullOrWhiteSpace(s.Theme) ? "Dark" : s.Theme;
            SelectedTheme = GetLocalizedTheme(Theme);

            Language = string.IsNullOrWhiteSpace(s.Language) ? "FR" : s.Language;
            GlobalHotkey = string.IsNullOrWhiteSpace(s.GlobalHotkey) ? "Ctrl+Shift+O" : s.GlobalHotkey;
            if (s.MaxConcurrentDownloads <= 0 || s.MaxConcurrentDownloads == int.MaxValue)
            {
                IsMaxConcurrentUnlimited = true;
                MaxConcurrentDownloads = 2;
            }
            else
            {
                IsMaxConcurrentUnlimited = false;
                MaxConcurrentDownloads = s.MaxConcurrentDownloads;
            }

            SpeedLimit = s.SpeedLimit;
            PlaylistNamingTemplate = string.IsNullOrWhiteSpace(s.PlaylistNamingTemplate) ? "{index} - {title}" : s.PlaylistNamingTemplate;
            
            LiveEngine = s.LiveEngine;
            LiveRecordMode = s.LiveRecordMode;
            LiveMaxDuration = s.LiveMaxDuration;
            LiveInterruptionAction = s.LiveInterruptionAction;

            if (string.IsNullOrWhiteSpace(LiveMaxDuration) ||
                LiveMaxDuration.Equals("Illimité", StringComparison.OrdinalIgnoreCase) ||
                LiveMaxDuration.Equals("Unlimited", StringComparison.OrdinalIgnoreCase) ||
                LiveMaxDuration.Equals("Ilimitado", StringComparison.OrdinalIgnoreCase))
            {
                IsLiveDurationUnlimited = true;
                LiveMaxDurationCustom = "60 min";
                LiveDurationValidationMessage = string.Empty;
            }
            else
            {
                IsLiveDurationUnlimited = false;
                LiveMaxDurationCustom = LiveMaxDuration;
                ValidateLiveDurationCustom(LiveMaxDurationCustom);
            }

            AdvancedVideoResolution = s.AdvancedVideoResolution;
            AdvancedVideoFps = s.AdvancedVideoFps;
            AdvancedVideoCodec = s.AdvancedVideoCodec;
            AdvancedVideoDynamicRange = s.AdvancedVideoDynamicRange;
            AdvancedVideoBitrate = s.AdvancedVideoBitrate;

            AdvancedAudioCodec = s.AdvancedAudioCodec;
            AdvancedAudioBitrate = s.AdvancedAudioBitrate;
            AdvancedAudioSampleRate = s.AdvancedAudioSampleRate;
            AdvancedAudioBitDepth = s.AdvancedAudioBitDepth;
            AdvancedAudioChannels = s.AdvancedAudioChannels;

            AutoSniff = s.AutoSniff;
            ExtractionBrowser = string.IsNullOrWhiteSpace(s.ExtractionBrowser) ? "Chromium" : s.ExtractionBrowser;
            ClipboardWatch = s.ClipboardWatch;
            NotificationsEnabled = s.NotificationsEnabled;
            EmbedThumbnail = s.EmbedThumbnail;
            AutoResumeOnStartup = s.AutoResumeOnStartup;
            
            if (s.MaxRetries < 0 || s.MaxRetries == int.MaxValue)
            {
                IsMaxRetriesUnlimited = true;
                MaxRetries = 3;
            }
            else
            {
                IsMaxRetriesUnlimited = false;
                MaxRetries = Math.Clamp(s.MaxRetries, 0, 10);
            }

            IsSuccessBubbleVisible = false;
            LocalizationService.Instance.SetLanguage(Language);
            RefreshLocalizedOptions();
        }
        finally
        {
            _isRefreshingOptions = false;
        }

        TakeSnapshot();
        RefreshLogs();
    }

    private void TakeSnapshot()
    {
        _initialSnapshot = new SettingsSnapshot(
            DownloadFolder ?? string.Empty,
            SelectedTheme ?? string.Empty,
            Language ?? string.Empty,
            MaxConcurrentDownloads,
            IsMaxConcurrentUnlimited,
            SpeedLimit ?? string.Empty,
            PlaylistNamingTemplate ?? "{index} - {title}",
            LiveEngine ?? string.Empty,
            LiveRecordMode ?? string.Empty,
            IsLiveDurationUnlimited,
            LiveMaxDurationCustom ?? string.Empty,
            LiveInterruptionAction ?? string.Empty,
            AdvancedVideoResolution ?? string.Empty,
            AdvancedVideoFps ?? string.Empty,
            AdvancedVideoCodec ?? string.Empty,
            AdvancedVideoDynamicRange ?? string.Empty,
            AdvancedVideoBitrate ?? string.Empty,
            AdvancedAudioCodec ?? string.Empty,
            AdvancedAudioBitrate ?? string.Empty,
            AdvancedAudioSampleRate ?? string.Empty,
            AdvancedAudioBitDepth ?? string.Empty,
            AdvancedAudioChannels ?? string.Empty,
            AutoSniff,
            ExtractionBrowser ?? "Chromium",
            ClipboardWatch,
            NotificationsEnabled,
            EmbedThumbnail,
            AutoResumeOnStartup,
            MaxRetries,
            IsMaxRetriesUnlimited,
            GlobalHotkey ?? "Ctrl+Shift+O"
        );
        RecalculatePendingChanges();
    }

    private void TranslateSnapshotToCurrentLanguage()
    {
        if (_initialSnapshot == null) return;
        _initialSnapshot = _initialSnapshot with
        {
            SelectedTheme = Loc.TranslateOption(_initialSnapshot.SelectedTheme),
            SpeedLimit = Loc.TranslateOption(_initialSnapshot.SpeedLimit),
            LiveEngine = Loc.TranslateOption(_initialSnapshot.LiveEngine),
            LiveRecordMode = Loc.TranslateOption(_initialSnapshot.LiveRecordMode),
            LiveInterruptionAction = Loc.TranslateOption(_initialSnapshot.LiveInterruptionAction),
            AdvancedVideoResolution = Loc.TranslateOption(_initialSnapshot.AdvancedVideoResolution),
            AdvancedVideoFps = Loc.TranslateOption(_initialSnapshot.AdvancedVideoFps),
            AdvancedVideoCodec = Loc.TranslateOption(_initialSnapshot.AdvancedVideoCodec),
            AdvancedVideoDynamicRange = Loc.TranslateOption(_initialSnapshot.AdvancedVideoDynamicRange),
            AdvancedVideoBitrate = Loc.TranslateOption(_initialSnapshot.AdvancedVideoBitrate),
            AdvancedAudioCodec = Loc.TranslateOption(_initialSnapshot.AdvancedAudioCodec),
            AdvancedAudioBitrate = Loc.TranslateOption(_initialSnapshot.AdvancedAudioBitrate),
            AdvancedAudioSampleRate = Loc.TranslateOption(_initialSnapshot.AdvancedAudioSampleRate),
            AdvancedAudioBitDepth = Loc.TranslateOption(_initialSnapshot.AdvancedAudioBitDepth),
            AdvancedAudioChannels = Loc.TranslateOption(_initialSnapshot.AdvancedAudioChannels)
        };
    }

    private void RecalculatePendingChanges()
    {
        if (_initialSnapshot == null)
        {
            PendingChangesCount = 0;
            HasPendingChanges = false;
            PendingChangesBannerText = string.Empty;
            return;
        }

        int count = 0;
        if (!string.Equals(DownloadFolder ?? string.Empty, _initialSnapshot.DownloadFolder, StringComparison.Ordinal)) count++;
        if (!string.Equals(SelectedTheme ?? string.Empty, _initialSnapshot.SelectedTheme, StringComparison.Ordinal)) count++;
        if (!string.Equals(Language ?? string.Empty, _initialSnapshot.Language, StringComparison.Ordinal)) count++;
        if (!string.Equals(GlobalHotkey ?? string.Empty, _initialSnapshot.GlobalHotkey, StringComparison.Ordinal)) count++;
        if (IsMaxConcurrentUnlimited != _initialSnapshot.IsMaxConcurrentUnlimited) count++;
        if (!IsMaxConcurrentUnlimited && MaxConcurrentDownloads != _initialSnapshot.MaxConcurrentDownloads) count++;
        if (!string.Equals(SpeedLimit ?? string.Empty, _initialSnapshot.SpeedLimit, StringComparison.Ordinal)) count++;
        if (!string.Equals(PlaylistNamingTemplate ?? string.Empty, _initialSnapshot.PlaylistNamingTemplate, StringComparison.Ordinal)) count++;
        if (!string.Equals(LiveEngine ?? string.Empty, _initialSnapshot.LiveEngine, StringComparison.Ordinal)) count++;
        if (!string.Equals(LiveRecordMode ?? string.Empty, _initialSnapshot.LiveRecordMode, StringComparison.Ordinal)) count++;
        if (IsLiveDurationUnlimited != _initialSnapshot.IsLiveDurationUnlimited) count++;
        if (!IsLiveDurationUnlimited && !string.Equals(LiveMaxDurationCustom ?? string.Empty, _initialSnapshot.LiveMaxDurationCustom, StringComparison.Ordinal)) count++;
        if (!string.Equals(LiveInterruptionAction ?? string.Empty, _initialSnapshot.LiveInterruptionAction, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedVideoResolution ?? string.Empty, _initialSnapshot.AdvancedVideoResolution, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedVideoFps ?? string.Empty, _initialSnapshot.AdvancedVideoFps, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedVideoCodec ?? string.Empty, _initialSnapshot.AdvancedVideoCodec, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedVideoDynamicRange ?? string.Empty, _initialSnapshot.AdvancedVideoDynamicRange, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedVideoBitrate ?? string.Empty, _initialSnapshot.AdvancedVideoBitrate, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedAudioCodec ?? string.Empty, _initialSnapshot.AdvancedAudioCodec, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedAudioBitrate ?? string.Empty, _initialSnapshot.AdvancedAudioBitrate, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedAudioSampleRate ?? string.Empty, _initialSnapshot.AdvancedAudioSampleRate, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedAudioBitDepth ?? string.Empty, _initialSnapshot.AdvancedAudioBitDepth, StringComparison.Ordinal)) count++;
        if (!string.Equals(AdvancedAudioChannels ?? string.Empty, _initialSnapshot.AdvancedAudioChannels, StringComparison.Ordinal)) count++;
        if (AutoSniff != _initialSnapshot.AutoSniff) count++;
        if (!string.Equals(ExtractionBrowser ?? string.Empty, _initialSnapshot.ExtractionBrowser, StringComparison.Ordinal)) count++;
        if (ClipboardWatch != _initialSnapshot.ClipboardWatch) count++;
        if (NotificationsEnabled != _initialSnapshot.NotificationsEnabled) count++;
        if (EmbedThumbnail != _initialSnapshot.EmbedThumbnail) count++;
        if (AutoResumeOnStartup != _initialSnapshot.AutoResumeOnStartup) count++;
        if (IsMaxRetriesUnlimited != _initialSnapshot.IsMaxRetriesUnlimited) count++;
        if (!IsMaxRetriesUnlimited && MaxRetries != _initialSnapshot.MaxRetries) count++;

        PendingChangesCount = count;
        HasPendingChanges = count > 0;

        if (count == 1)
        {
            PendingChangesBannerText = LocalizationService.Instance["PendingChangesSingular"];
        }
        else if (count > 1)
        {
            PendingChangesBannerText = string.Format(LocalizationService.Instance["PendingChangesPlural"], count);
        }
        else
        {
            PendingChangesBannerText = string.Empty;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_isRefreshingOptions &&
            _initialSnapshot != null &&
            e.PropertyName != nameof(PendingChangesCount) &&
            e.PropertyName != nameof(HasPendingChanges) &&
            e.PropertyName != nameof(PendingChangesBannerText) &&
            e.PropertyName != nameof(IsSuccessBubbleVisible) &&
            e.PropertyName != nameof(SuccessBubbleTitle) &&
            e.PropertyName != nameof(SuccessBubbleSubtitle) &&
            e.PropertyName != nameof(StatusMessage))
        {
            RecalculatePendingChanges();
        }
    }

    public bool IsLiveDurationCustomEnabled => !IsLiveDurationUnlimited;
    public bool HasLiveDurationError => !string.IsNullOrWhiteSpace(LiveDurationValidationMessage);

    public bool IsMaxConcurrentCustomEnabled => !IsMaxConcurrentUnlimited;
    public bool IsMaxRetriesCustomEnabled => !IsMaxRetriesUnlimited;

    partial void OnIsMaxConcurrentUnlimitedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMaxConcurrentCustomEnabled));
    }

    partial void OnIsMaxRetriesUnlimitedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMaxRetriesCustomEnabled));
    }

    partial void OnLanguageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        LocalizationService.Instance.SetLanguage(value);
        RefreshLocalizedOptions();
        TranslateSnapshotToCurrentLanguage();
        RecalculatePendingChanges();
    }

    partial void OnIsLiveDurationUnlimitedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLiveDurationCustomEnabled));
        if (value)
        {
            LiveMaxDuration = Loc["OptionUnlimited"];
            LiveDurationValidationMessage = string.Empty;
            OnPropertyChanged(nameof(HasLiveDurationError));
        }
        else
        {
            ValidateLiveDurationCustom(LiveMaxDurationCustom);
        }
    }

    partial void OnLiveMaxDurationCustomChanged(string value)
    {
        if (!IsLiveDurationUnlimited)
        {
            ValidateLiveDurationCustom(value);
        }
    }

    public bool ValidateLiveDurationCustom(string text)
    {
        if (LiveDurationHelper.TryParseLiveDuration(text, out var duration, out var error))
        {
            LiveMaxDuration = text;
            LiveDurationValidationMessage = string.Empty;
            OnPropertyChanged(nameof(HasLiveDurationError));
            return true;
        }
        else
        {
            LiveDurationValidationMessage = error;
            OnPropertyChanged(nameof(HasLiveDurationError));
            return false;
        }
    }

    [RelayCommand]
    private void DismissSuccessBubble()
    {
        IsSuccessBubbleVisible = false;
    }

    [RelayCommand]
    private void IncrementConcurrent()
    {
        if (IsMaxConcurrentUnlimited) return;
        MaxConcurrentDownloads++;
    }

    [RelayCommand]
    private void DecrementConcurrent()
    {
        if (IsMaxConcurrentUnlimited) return;
        if (MaxConcurrentDownloads > 1)
        {
            MaxConcurrentDownloads--;
        }
    }

    [RelayCommand]
    private void IncrementRetries()
    {
        if (IsMaxRetriesUnlimited) return;
        if (MaxRetries < 10)
        {
            MaxRetries++;
        }
    }

    [RelayCommand]
    private void DecrementRetries()
    {
        if (IsMaxRetriesUnlimited) return;
        if (MaxRetries > 0)
        {
            MaxRetries--;
        }
    }

    private int GetCurrentLiveDurationMinutes()
    {
        if (LiveDurationHelper.TryParseLiveDuration(LiveMaxDurationCustom, out var duration, out _) && duration.HasValue)
        {
            return (int)Math.Round(duration.Value.TotalMinutes);
        }
        var m = System.Text.RegularExpressions.Regex.Match(LiveMaxDurationCustom ?? string.Empty, @"\d+");
        if (m.Success && int.TryParse(m.Value, out int minVal))
        {
            return minVal;
        }
        return 60;
    }

    [RelayCommand]
    private void IncrementLiveDuration()
    {
        if (IsLiveDurationUnlimited) return;
        int current = GetCurrentLiveDurationMinutes();
        int next = Math.Clamp(current + 1, 1, 300);
        LiveMaxDurationCustom = $"{next} min";
    }

    [RelayCommand]
    private void DecrementLiveDuration()
    {
        if (IsLiveDurationUnlimited) return;
        int current = GetCurrentLiveDurationMinutes();
        int next = Math.Clamp(current - 1, 1, 300);
        LiveMaxDurationCustom = $"{next} min";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Sélectionnez le dossier de téléchargement",
            InitialDirectory = DownloadFolder
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settingsService.Settings;
        s.DownloadFolder = DownloadFolder;
        s.DefaultQuality = DefaultQuality;

        s.Theme = GetCanonicalTheme(SelectedTheme);
        Theme = s.Theme;
        UberDownloader.Wpf.Services.ThemeService.ApplyTheme(s.Theme);

        s.Language = Language;
        s.MaxConcurrentDownloads = IsMaxConcurrentUnlimited ? 0 : Math.Max(1, MaxConcurrentDownloads);
        s.SpeedLimit = SpeedLimit;
        s.PlaylistNamingTemplate = PlaylistNamingTemplate;
        
        s.LiveEngine = LiveEngine;
        s.LiveRecordMode = LiveRecordMode;
        
        if (IsLiveDurationUnlimited)
        {
            s.LiveMaxDuration = Loc["OptionUnlimited"];
            LiveMaxDuration = Loc["OptionUnlimited"];
            LiveDurationValidationMessage = string.Empty;
        }
        else
        {
            if (ValidateLiveDurationCustom(LiveMaxDurationCustom))
            {
                s.LiveMaxDuration = LiveMaxDurationCustom;
                LiveMaxDuration = LiveMaxDurationCustom;
            }
            else
            {
                s.LiveMaxDuration = "60 min";
                LiveMaxDuration = "60 min";
            }
        }
        s.LiveInterruptionAction = LiveInterruptionAction;

        s.AdvancedVideoResolution = AdvancedVideoResolution;
        s.AdvancedVideoFps = AdvancedVideoFps;
        s.AdvancedVideoCodec = AdvancedVideoCodec;
        s.AdvancedVideoDynamicRange = AdvancedVideoDynamicRange;
        s.AdvancedVideoBitrate = AdvancedVideoBitrate;

        s.AdvancedAudioCodec = AdvancedAudioCodec;
        s.AdvancedAudioBitrate = AdvancedAudioBitrate;
        s.AdvancedAudioSampleRate = AdvancedAudioSampleRate;
        s.AdvancedAudioBitDepth = AdvancedAudioBitDepth;
        s.AdvancedAudioChannels = AdvancedAudioChannels;

        s.AutoSniff = AutoSniff;
        s.ExtractionBrowser = ExtractionBrowser;
        s.ClipboardWatch = ClipboardWatch;
        s.NotificationsEnabled = NotificationsEnabled;
        s.EmbedThumbnail = EmbedThumbnail;
        s.AutoResumeOnStartup = AutoResumeOnStartup;
        s.MaxRetries = IsMaxRetriesUnlimited ? -1 : Math.Clamp(MaxRetries, 0, 10);
        s.GlobalHotkey = string.IsNullOrWhiteSpace(GlobalHotkey) ? "Ctrl+Shift+O" : GlobalHotkey.Trim();

        _settingsService.SaveSettings(s);
        _downloadScheduler.UpdateConcurrency(s.MaxConcurrentDownloads);
        if (System.Windows.Application.Current is App app)
        {
            app.ApplyGlobalHotkey(s.GlobalHotkey);
        }

        string displayConcurrent = IsMaxConcurrentUnlimited ? Loc["OptionUnlimited"] : MaxConcurrentDownloads.ToString();
        string displayRetries = IsMaxRetriesUnlimited ? Loc["OptionUnlimited"] : MaxRetries.ToString();
        Logger.Log(Loc.Format("LogSettingsSaved", Language, SpeedLimit, s.GlobalHotkey, displayConcurrent, displayRetries));

        // Met à jour le snapshot : la barre transversale disparaît immédiatement après le clic sur Enregistrer
        TakeSnapshot();

        // Afficher la bulle de confirmation et RESTER dans la page des paramètres
        SuccessBubbleTitle = LocalizationService.Instance["SettingsSavedTitle"];
        SuccessBubbleSubtitle = LocalizationService.Instance["SettingsSavedSubtitle"];
        IsSuccessBubbleVisible = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsSuccessBubbleVisible = false;
        LoadSettings();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void RefreshLogs()
    {
        string raw = Logger.ReadAllLogs();
        LogsRefreshed?.Invoke(raw);
        var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        LogEntries.Clear();

        for (int i = 0; i < lines.Length; i++)
        {
            LogEntries.Add(new LogEntry
            {
                Text = lines[i],
                IsLast = (i == lines.Length - 1)
            });
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logger.ClearLogs();
        RefreshLogs();
    }

    [RelayCommand]
    private void OpenAppData()
    {
        string appData = AppPaths.DataDirectory;
        if (Directory.Exists(appData))
        {
            Process.Start("explorer.exe", appData);
        }
    }

    [RelayCommand]
    private async Task UpdateYtDlpAsync()
    {
        StatusMessage = "🔄 Vérification et mise à jour de yt-dlp en cours...";
        bool success = await AutoUpdateService.ForceUpdateYtDlpAsync();
        StatusMessage = success ? "✅ yt-dlp a été mis à jour avec succès !" : "⚠️ Échec de la mise à jour ou connexion impossible.";
        RefreshLogs();
    }

    [RelayCommand]
    private void OpenKofi()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/yddraiggoch",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[KOFI] Impossible d'ouvrir le lien Ko-fi : {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenThirdPartyNotices()
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string noticesPath = Path.Combine(appDir, "THIRD-PARTY-NOTICES.txt");
            
            if (!File.Exists(noticesPath))
            {
                string candidate = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "THIRD-PARTY-NOTICES.txt"));
                if (File.Exists(candidate)) noticesPath = candidate;
            }

            if (File.Exists(noticesPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = noticesPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ABOUT] Impossible d'ouvrir les notices : {ex.Message}");
        }
    }
}
