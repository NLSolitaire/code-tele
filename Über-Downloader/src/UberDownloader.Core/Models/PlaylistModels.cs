using System.Collections.Generic;

namespace UberDownloader.Core.Models;

public class PlaylistEntryInfo
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public string UnavailableReason { get; set; } = string.Empty;
}

public class PlaylistInspectionResult
{
    public string SourceUrl { get; set; } = string.Empty;
    public string PlaylistId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Uploader { get; set; } = string.Empty;
    public int TotalVideos { get; set; } = 0;
    public List<PlaylistEntryInfo> Entries { get; set; } = new();
}
