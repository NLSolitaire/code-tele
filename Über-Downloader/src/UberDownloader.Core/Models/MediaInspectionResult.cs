using System;
using System.Collections.Generic;

namespace UberDownloader.Core.Models;

public class AudioQualityTier
{
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int TargetBitrateKbps { get; set; }
    public string Audience { get; set; } = string.Empty;
    public bool IsLossless { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string UnavailableReason { get; set; } = string.Empty;
    public string EstimatedSizeStr { get; set; } = string.Empty;

    // Pour l'affichage dans le menu déroulant sans le libellé "Pour qui"
    public string DisplayText => string.IsNullOrEmpty(Format) ? Name : $"{Name} ({Format})";
}

public class MediaInspectionResult
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public double? DurationSeconds { get; set; }
    public bool IsLive { get; set; }
    public string Extractor { get; set; } = string.Empty;

    public int MaxAvailableAudioBitrateKbps { get; set; } = 128;
    public string SourceAudioCodec { get; set; } = string.Empty;
    public int? SourceAudioSampleRate { get; set; }

    public List<string> AvailableResolutions { get; set; } = new();
    public List<string> AvailableVideoCodecs { get; set; } = new();
    public List<string> AvailableContainers { get; set; } = new() { "Recommandé", "MP4", "MKV", "WebM" };
    public List<string> AvailableAudioLanguages { get; set; } = new() { "Originale" };
    public List<string> AvailableSubtitles { get; set; } = new() { "Aucun" };
    public List<AudioQualityTier> AudioTiers { get; set; } = new();
    public Dictionary<string, string> VideoEstimatedSizes { get; set; } = new();
}
