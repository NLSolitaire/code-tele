using System;

namespace UberDownloader.Core.Models;

public enum JobStatus
{
    Queued,
    Scheduled,
    Analyzing,
    Downloading,
    Paused,
    Processing,
    Finished,
    Error,
    Cancelled,
    LiveInterrupted,
    Ignored
}

public class DownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? PlaylistId { get; set; }
    public int PlaylistIndex { get; set; } = 0;
    public string PlaylistFolder { get; set; } = string.Empty;
    public int Attempts { get; set; } = 0;
    public string LastError { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Referer { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CustomTitle { get; set; } = string.Empty;
    public string Quality { get; set; } = "1080p Full HD";
    public string Codec { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string StatusDetail { get; set; } = string.Empty;
    public double Progress { get; set; } = 0.0;
    public long DownloadedBytes { get; set; } = 0;
    public long TotalBytes { get; set; } = 0;
    public string SpeedStr { get; set; } = string.Empty;
    public string EtaStr { get; set; } = string.Empty;
    public string SizeStr { get; set; } = "--";
    public string OutputPath { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsLive { get; set; } = false;
    public string LiveDurationStr { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
    public string? ScheduleType { get; set; } // "delay", "internet"
    public DateTime? ScheduledTime { get; set; }

    // Paramètres choisis pour ce fichier
    public string AudioLanguage { get; set; } = string.Empty;
    public string SubtitleTrack { get; set; } = string.Empty;
    public bool EmbedSubtitles { get; set; } = true;
    public string VideoResolution { get; set; } = string.Empty;
    public string VideoFps { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string VideoDynamicRange { get; set; } = string.Empty;
    public string ContainerFormat { get; set; } = string.Empty;
    public string SpeedLimit { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string AudioBitrate { get; set; } = string.Empty;
    public string AudioSampleRate { get; set; } = string.Empty;
    public string AudioChannels { get; set; } = string.Empty;

    // Surcharge de gabarit de nommage de playlist
    public string NamingTemplate { get; set; } = string.Empty;

    // Enregistrement des directs (Lives)
    public string LiveMaxDuration { get; set; } = "Illimité";
    public string TempTsPath { get; set; } = string.Empty;
    public bool IsStopLiveRequested { get; set; } = false;
}

