using System;
using System.Collections.Generic;
using System.IO;

namespace UberDownloader.Core.Models;

public class AppSettings
{
    public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public string DefaultQuality { get; set; } = "1080p Full HD";
    public string Theme { get; set; } = "System"; // Dark, Light, System
    public string Language { get; set; } = "FR";  // FR, EN, ES
    public int MaxConcurrentDownloads { get; set; } = 2;
    public string SpeedLimit { get; set; } = "Illimité"; // 500 Ko/s, 1 Mo/s, 5 Mo/s, 10 Mo/s, 20 Mo/s, 50 Mo/s, 100 Mo/s, Illimité
    public string PlaylistNamingTemplate { get; set; } = "{index} - {title}";
    public bool AutoResumeOnStartup { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
    public bool AutoSniff { get; set; } = true;
    public string ExtractionBrowser { get; set; } = "Chromium";
    public string BrowserCookies { get; set; } = "aucun";
    
    // Lives / Directs
    public string LiveEngine { get; set; } = "Streamlink (recommandé)"; // Streamlink (recommandé), yt-dlp
    public string LiveRecordMode { get; set; } = "Enregistrer les directs depuis le clic"; // Enregistrer les directs depuis le début, Enregistrer les directs depuis le clic
    public string LiveMaxDuration { get; set; } = "Illimité";
    public string LiveInterruptionAction { get; set; } = "Demander à chaque fois";
    
    // Paramètres avancés Vidéo
    public string AdvancedVideoResolution { get; set; } = "Automatique";
    public string AdvancedVideoFps { get; set; } = "Conserver celle de la source";
    public string AdvancedVideoCodec { get; set; } = "Automatique";
    public string AdvancedVideoDynamicRange { get; set; } = "Automatique";
    public string AdvancedVideoBitrate { get; set; } = "Automatique";

    // Paramètres avancés Audio
    public string AdvancedAudioCodec { get; set; } = "Automatique";
    public string AdvancedAudioBitrate { get; set; } = "Automatique";
    public string AdvancedAudioSampleRate { get; set; } = "Conserver celle de la source";
    public string AdvancedAudioBitDepth { get; set; } = "Conserver celle de la source";
    public string AdvancedAudioChannels { get; set; } = "Automatique";

    public bool ClipboardWatch { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public string GlobalHotkey { get; set; } = "Ctrl+Shift+O";
    public HashSet<string> IgnoredDuplicateUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
