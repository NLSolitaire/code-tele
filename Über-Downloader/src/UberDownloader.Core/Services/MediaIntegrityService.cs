using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UberDownloader.Core.Services;

public class MediaVerificationResult
{
    public bool IsValid { get; set; }
    public double DurationSeconds { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public string ResolutionTier { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class MediaIntegrityService
{
    public async Task<MediaVerificationResult> VerifyMediaFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var result = new MediaVerificationResult();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            result.IsValid = false;
            result.ErrorMessage = "Fichier introuvable.";
            return result;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            result.FileSizeBytes = fileInfo.Length;

            if (fileInfo.Length < 1024)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Taille de fichier anormalement faible ({fileInfo.Length} octets).";
                return result;
            }

            string ffprobeExe = ToolLocator.GetFfprobePath();
            if (!File.Exists(ffprobeExe) && !string.Equals(ffprobeExe, "ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback si ffprobe n'est pas encore installé ou introuvable : validation de base
                result.IsValid = fileInfo.Length > 1024;
                result.ResolutionTier = "Inconnue";
                return result;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffprobeExe,
                Arguments = $"-v error -show_entries format=duration,size,bit_rate -show_entries stream=index,codec_type,codec_name,width,height -of json \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

            await proc.WaitForExitAsync(cancellationToken);

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            {
                result.IsValid = false;
                result.ErrorMessage = string.IsNullOrWhiteSpace(stderr) ? "Erreur ffprobe inconnue." : stderr.Trim();
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;

                // 1. Durée dans 'format'
                if (root.TryGetProperty("format", out var formatElem))
                {
                    if (formatElem.TryGetProperty("duration", out var durElem) &&
                        double.TryParse(durElem.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double durSec))
                    {
                        result.DurationSeconds = durSec;
                    }
                }

                // 2. Flux (streams)
                bool hasStreams = false;
                if (root.TryGetProperty("streams", out var streamsElem) && streamsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in streamsElem.EnumerateArray())
                    {
                        hasStreams = true;
                        string type = s.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
                        string codec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";

                        if (type.Equals("video", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.VideoCodec))
                        {
                            result.VideoCodec = codec;
                            int w = s.TryGetProperty("width", out var wp) ? wp.GetInt32() : 0;
                            int h = s.TryGetProperty("height", out var hp) ? hp.GetInt32() : 0;
                            if (w > 0 && h > 0)
                            {
                                result.Resolution = $"{w}x{h}";
                                result.ResolutionTier = FormatResolutionTier(w, h);
                            }
                        }
                        else if (type.Equals("audio", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(result.AudioCodec))
                        {
                            result.AudioCodec = codec;
                        }
                    }
                }

                // Pour un fichier multimédia valide, il doit comporter au moins un flux et une durée > 0
                if (!hasStreams)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "Aucun flux audio ou vidéo détecté dans le conteneur.";
                    return result;
                }

                if (result.DurationSeconds <= 0.1)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "La durée détectée est nulle ou invalide.";
                    return result;
                }

                result.IsValid = true;
                return result;
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Sortie ffprobe non analysable : {ex.Message}";
                return result;
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    public static string FormatResolutionTier(int width, int height)
    {
        int minDim = Math.Min(width, height);
        if (minDim >= 2160 || width >= 3800) return "4K (2160p)";
        if (minDim >= 1440 || width >= 2500) return "1440p (QHD)";
        if (minDim >= 1080 || width >= 1900) return "1080p (Full HD)";
        if (minDim >= 720 || width >= 1200) return "720p (HD)";
        if (minDim >= 480) return "480p";
        if (minDim >= 360) return "360p";
        return $"{height}p";
    }

    public static int GetResolutionRank(string? resolutionStr)
    {
        if (string.IsNullOrWhiteSpace(resolutionStr)) return 0;
        string lower = resolutionStr.ToLowerInvariant();
        if (lower.Contains("4320") || lower.Contains("8k")) return 8000;
        if (lower.Contains("2160") || lower.Contains("4k")) return 4000;
        if (lower.Contains("1440")) return 2500;
        if (lower.Contains("1080")) return 1080;
        if (lower.Contains("720")) return 720;
        if (lower.Contains("480")) return 480;
        if (lower.Contains("360")) return 360;
        return 100;
    }
}
