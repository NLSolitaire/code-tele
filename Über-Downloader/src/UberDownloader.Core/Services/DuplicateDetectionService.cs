using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UberDownloader.Core.Data;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public enum DuplicateStatus
{
    None,
    IdenticalOrHigherQuality,
    LowerQuality,
    InQueue
}

public class DuplicateCheckResult
{
    public DuplicateStatus Status { get; set; } = DuplicateStatus.None;
    public string ExistingFilePath { get; set; } = string.Empty;
    public string ExistingQuality { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public bool ShouldDeselectByDefault => Status == DuplicateStatus.IdenticalOrHigherQuality;
}

public class DuplicateDetectionService
{
    private readonly MediaIntegrityService _integrityService;

    public DuplicateDetectionService(MediaIntegrityService integrityService)
    {
        _integrityService = integrityService;
    }

    public static string ExtractVideoKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        url = url.Trim();

        // 1. YouTube standard & shorts
        var ytMatch = Regex.Match(url, @"(?:v=|/v/|youtu\.be/|/embed/|/shorts/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase);
        if (ytMatch.Success) return $"yt_{ytMatch.Groups[1].Value}";

        // 2. Vimeo
        var vimeoMatch = Regex.Match(url, @"vimeo\.com/(?:video/)?(\d+)", RegexOptions.IgnoreCase);
        if (vimeoMatch.Success) return $"vimeo_{vimeoMatch.Groups[1].Value}";

        // 3. Dailymotion
        var dmMatch = Regex.Match(url, @"dailymotion\.com/video/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
        if (dmMatch.Success) return $"dm_{dmMatch.Groups[1].Value}";

        // 4. TikTok
        var tikMatch = Regex.Match(url, @"tiktok\.com/@[\w\.-]+/video/(\d+)", RegexOptions.IgnoreCase);
        if (tikMatch.Success) return $"tiktok_{tikMatch.Groups[1].Value}";

        // Fallback : URL nettoyée sans query strings superflues
        try
        {
            var uri = new Uri(url);
            return $"{uri.Host}{uri.AbsolutePath}".ToLowerInvariant();
        }
        catch
        {
            return url.ToLowerInvariant();
        }
    }

    public async Task<DuplicateCheckResult> CheckDuplicateAsync(
        string url,
        string title,
        int playlistIndex,
        string targetFolder,
        string requestedQuality,
        DatabaseService databaseService,
        CancellationToken cancellationToken = default)
    {
        var result = new DuplicateCheckResult();
        string cleanTitle = DownloadService.SanitizeFileName(!string.IsNullOrWhiteSpace(title) ? title : "video");
        string videoKey = ExtractVideoKey(url);

        // 1. Recherche par fichier physique dans targetFolder
        if (Directory.Exists(targetFolder))
        {
            string[] possiblePrefixes = playlistIndex > 0 
                ? new[] { $"{playlistIndex:D3} - {cleanTitle}", $"{playlistIndex:D3}. {cleanTitle}", cleanTitle } 
                : new[] { cleanTitle };

            foreach (var prefix in possiblePrefixes)
            {
                var candidates = Directory.GetFiles(targetFolder, $"{prefix}.*")
                                          .Where(f => DownloadService.IsMediaFile(f))
                                          .ToList();

                foreach (var candidate in candidates)
                {
                    var verify = await _integrityService.VerifyMediaFileAsync(candidate, cancellationToken);
                    if (verify.IsValid)
                    {
                        result.ExistingFilePath = candidate;
                        result.ExistingQuality = !string.IsNullOrWhiteSpace(verify.ResolutionTier) 
                            ? verify.ResolutionTier 
                            : (verify.DurationSeconds > 0 ? "Fichier complet" : "Existant");

                        int existingRank = MediaIntegrityService.GetResolutionRank(result.ExistingQuality);
                        int requestedRank = MediaIntegrityService.GetResolutionRank(requestedQuality);

                        if (existingRank >= requestedRank && existingRank > 0 && requestedRank > 0)
                        {
                            result.Status = DuplicateStatus.IdenticalOrHigherQuality;
                            result.BadgeText = string.Format(LocalizationService.Instance["DuplicateAlreadyDownloaded"], result.ExistingQuality);
                        }
                        else
                        {
                            result.Status = DuplicateStatus.LowerQuality;
                            result.BadgeText = string.Format(LocalizationService.Instance["DuplicateLowerQuality"], result.ExistingQuality);
                        }
                        return result;
                    }
                }
            }
        }

        // 2. Recherche dans la base SQLite si l'URL ou un élément terminé existe
        try
        {
            var dbItems = await databaseService.GetItemsAsync(DateFilter.All, searchQuery: cleanTitle, limit: 10, standaloneOnly: false);
            var match = dbItems.FirstOrDefault(i =>
                (!string.IsNullOrWhiteSpace(i.Url) && (i.Url.Equals(url, StringComparison.OrdinalIgnoreCase) || ExtractVideoKey(i.Url) == videoKey)) &&
                i.Status == JobStatus.Finished &&
                !string.IsNullOrWhiteSpace(i.OutputPath) &&
                File.Exists(i.OutputPath));

            if (match != null && File.Exists(match.OutputPath))
            {
                var verify = await _integrityService.VerifyMediaFileAsync(match.OutputPath, cancellationToken);
                if (verify.IsValid)
                {
                    result.ExistingFilePath = match.OutputPath;
                    result.ExistingQuality = !string.IsNullOrWhiteSpace(verify.ResolutionTier) ? verify.ResolutionTier : match.Quality;

                    int existingRank = MediaIntegrityService.GetResolutionRank(result.ExistingQuality);
                    int requestedRank = MediaIntegrityService.GetResolutionRank(requestedQuality);

                    if (existingRank >= requestedRank && existingRank > 0 && requestedRank > 0)
                    {
                        result.Status = DuplicateStatus.IdenticalOrHigherQuality;
                        result.BadgeText = string.Format(LocalizationService.Instance["DuplicateAlreadyDownloaded"], result.ExistingQuality);
                    }
                    else
                    {
                        result.Status = DuplicateStatus.LowerQuality;
                        result.BadgeText = string.Format(LocalizationService.Instance["DuplicateLowerQuality"], result.ExistingQuality);
                    }
                    return result;
                }
            }
        }
        catch { }

        return result;
    }
}
