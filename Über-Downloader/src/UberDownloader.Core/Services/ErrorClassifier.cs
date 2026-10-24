using System;
using System.Text.RegularExpressions;

namespace UberDownloader.Core.Services;

public enum ErrorCategory
{
    NetworkLoss,
    ServerTemporary,
    RateLimited,
    ContentUnavailable,
    FormatUnavailable,
    DiskFull,
    PostProcessingError,
    GenericUnknown
}

public class ErrorClassificationResult
{
    public ErrorCategory Category { get; set; } = ErrorCategory.GenericUnknown;
    public bool IsPermanent { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public int RetryDelaySeconds { get; set; }
    public bool ReduceConcurrency { get; set; }
    public bool ShouldPreservePartFiles { get; set; } = true;
}

public static class ErrorClassifier
{
    public const int DefaultMaxRetries = 3;

    public static ErrorClassificationResult Classify(string? rawError, int currentAttempts = 1, int maxRetries = DefaultMaxRetries)
    {
        var result = new ErrorClassificationResult();
        string err = rawError?.Trim() ?? string.Empty;

        // 1. Espace disque plein
        if (err.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Espace disque insuffisant", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.DiskFull;
            result.IsPermanent = true;
            result.ShouldPreservePartFiles = true;
            result.UserMessage = "Disque plein : espace de stockage insuffisant.";
            return result;
        }

        // 2. Contenu supprimé / privé / indisponible définitivement
        if (err.Contains("Private video", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("This video has been removed", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("This video is unavailable", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Premieres in", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("copyright claim", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Account terminated", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("This live stream recording is not available", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.ContentUnavailable;
            result.IsPermanent = true;
            result.ShouldPreservePartFiles = false;
            result.UserMessage = "Vidéo indisponible (privée, supprimée ou restreinte).";
            return result;
        }

        // 3. Rate Limit (HTTP 429 / Captcha)
        if (err.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("rate-limit", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("bot confirmation", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.RateLimited;
            result.ReduceConcurrency = true;
            result.ShouldPreservePartFiles = true;

            if (currentAttempts < maxRetries)
            {
                result.IsPermanent = false;
                result.RetryDelaySeconds = (int)Math.Min(120, Math.Pow(3, currentAttempts) * 5); // 15s, 45s...
                result.UserMessage = $"Limitation de requêtes détectée (HTTP 429). Nouvelle tentative dans {result.RetryDelaySeconds}s...";
            }
            else
            {
                result.IsPermanent = true;
                result.UserMessage = "Limitation de requêtes persistante de la plateforme (HTTP 429).";
            }
            return result;
        }

        // 4. Perte de connexion réseau
        if (err.Contains("WinError 10054", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Connection reset by peer", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Temporary failure in name resolution", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("IncompleteRead", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("RemoteDisconnected", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.NetworkLoss;
            result.ShouldPreservePartFiles = true;

            if (currentAttempts < maxRetries)
            {
                result.IsPermanent = false;
                result.RetryDelaySeconds = Math.Min(60, currentAttempts * 8); // 8s, 16s, 24s...
                result.UserMessage = $"Connexion interrompue. Nouvelle tentative ({currentAttempts}/{maxRetries}) dans {result.RetryDelaySeconds}s...";
            }
            else
            {
                result.IsPermanent = true;
                result.UserMessage = $"Erreur réseau persistante après {maxRetries} tentatives.";
            }
            return result;
        }

        // 5. Erreur serveur temporaire (500, 502, 503, 504)
        if (err.Contains("HTTP Error 500", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("HTTP Error 502", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("HTTP Error 503", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("HTTP Error 504", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Bad Gateway", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Gateway Timeout", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.ServerTemporary;
            result.ShouldPreservePartFiles = true;

            if (currentAttempts < maxRetries)
            {
                result.IsPermanent = false;
                result.RetryDelaySeconds = Math.Min(60, currentAttempts * 10);
                result.UserMessage = $"Erreur serveur temporaire. Nouvelle tentative dans {result.RetryDelaySeconds}s...";
            }
            else
            {
                result.IsPermanent = true;
                result.UserMessage = "Le serveur distant est temporairement indisponible.";
            }
            return result;
        }

        // 6. Format non disponible
        if (err.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("No video formats found", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.FormatUnavailable;
            result.IsPermanent = true;
            result.ShouldPreservePartFiles = false;
            result.UserMessage = "Format ou résolution demandée indisponible pour cette vidéo.";
            return result;
        }

        // 7. Échec d'assemblage / post-traitement ffmpeg
        if (err.Contains("ffmpeg failed", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("muxing error", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("unable to extract audio", StringComparison.OrdinalIgnoreCase) ||
            err.Contains("Merger failed", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = ErrorCategory.PostProcessingError;
            result.IsPermanent = true;
            result.ShouldPreservePartFiles = true; // Conserver les fragments pour pouvoir réassembler
            result.UserMessage = "Échec de fusion ou conversion vidéo/audio (ffmpeg).";
            return result;
        }

        // Défaut : Erreur générique
        result.Category = ErrorCategory.GenericUnknown;
        result.IsPermanent = currentAttempts >= maxRetries;
        result.RetryDelaySeconds = result.IsPermanent ? 0 : 5;
        result.ShouldPreservePartFiles = true;
        result.UserMessage = string.IsNullOrWhiteSpace(err) ? "Erreur de téléchargement inconnue." : err;
        return result;
    }
}
