using System;
using System.Collections.Generic;

namespace UberDownloader.Core.Models;

public class PlaylistJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string TargetFolder { get; set; } = string.Empty;
    public int TotalItems { get; set; } = 0;
    public string CustomTitle { get; set; } = string.Empty;

    public string NamingTemplate { get; set; } = string.Empty;
    public string PreferredQuality { get; set; } = string.Empty;

    // Statistiques calculées
    public int FinishedCount { get; set; } = 0;
    public int DownloadingCount { get; set; } = 0;
    public int QueuedCount { get; set; } = 0;
    public int ErrorCount { get; set; } = 0;
    public int IgnoredCount { get; set; } = 0;
    public double Progress { get; set; } = 0.0;
}

