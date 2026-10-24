using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public class MediaInspector
{
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = "Français",
        ["fra"] = "Français",
        ["fre"] = "Français",
        ["fr-fr"] = "Français (France)",
        ["fr-ca"] = "Français (Canada)",
        ["en"] = "Anglais",
        ["eng"] = "Anglais",
        ["en-us"] = "Anglais (US)",
        ["en-gb"] = "Anglais (UK)",
        ["es"] = "Espagnol",
        ["spa"] = "Espagnol",
        ["es-es"] = "Espagnol (Espagne)",
        ["es-419"] = "Espagnol (Amérique latine)",
        ["de"] = "Allemand",
        ["ger"] = "Allemand",
        ["deu"] = "Allemand",
        ["it"] = "Italien",
        ["ita"] = "Italien",
        ["pt"] = "Portugais",
        ["por"] = "Portugais",
        ["pt-br"] = "Portugais (Brésil)",
        ["pt-pt"] = "Portugais (Portugal)",
        ["ja"] = "Japonais",
        ["jpn"] = "Japonais",
        ["ko"] = "Coréen",
        ["kor"] = "Coréen",
        ["zh"] = "Chinois",
        ["zho"] = "Chinois",
        ["zh-hans"] = "Chinois simplifié",
        ["zh-hant"] = "Chinois traditionnel",
        ["ru"] = "Russe",
        ["rus"] = "Russe",
        ["ar"] = "Arabe",
        ["ara"] = "Arabe",
        ["hi"] = "Hindi",
        ["hin"] = "Hindi",
        ["id"] = "Indonésien",
        ["ind"] = "Indonésien",
        ["vi"] = "Vietnamien",
        ["vie"] = "Vietnamien",
        ["th"] = "Thaï",
        ["tha"] = "Thaï",
        ["bn"] = "Bengali",
        ["ben"] = "Bengali",
        ["tr"] = "Turc",
        ["tur"] = "Turc",
        ["nl"] = "Néerlandais",
        ["nld"] = "Néerlandais",
        ["dut"] = "Néerlandais",
        ["pl"] = "Polonais",
        ["pol"] = "Polonais",
        ["sv"] = "Suédois",
        ["swe"] = "Suédois",
        ["da"] = "Danois",
        ["dan"] = "Danois",
        ["fi"] = "Finnois",
        ["fin"] = "Finnois",
        ["no"] = "Norvégien",
        ["nor"] = "Norvégien",
        ["cs"] = "Tchèque",
        ["ces"] = "Tchèque",
        ["cze"] = "Tchèque",
        ["el"] = "Grec",
        ["ell"] = "Grec",
        ["gre"] = "Grec",
        ["uk"] = "Ukrainien",
        ["ukr"] = "Ukrainien",
        ["ro"] = "Roumain",
        ["ron"] = "Roumain",
        ["rum"] = "Roumain",
        ["hu"] = "Hongrois",
        ["hun"] = "Hongrois",
        ["mr"] = "Marathi",
        ["mar"] = "Marathi",
        ["ta"] = "Tamoul",
        ["tam"] = "Tamoul",
        ["te"] = "Télougou",
        ["tel"] = "Télougou",
        ["pa"] = "Pendjabi",
        ["pan"] = "Pendjabi",
        ["fa"] = "Persan",
        ["fas"] = "Persan",
        ["per"] = "Persan",
        ["ur"] = "Ourdou",
        ["urd"] = "Ourdou"
    };

    public async Task<MediaInspectionResult> InspectUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new MediaInspectionResult { Url = url };

        // 1. Détection rapide de Live Stream pour Twitch / Kick
        string lower = url.ToLowerInvariant();
        if (lower.Contains("twitch.tv") || lower.Contains("kick.com"))
        {
            result.IsLive = true;
            result.Extractor = lower.Contains("twitch.tv") ? "twitch" : "kick";

            string channel = string.Empty;
            try
            {
                var uri = new Uri(url);
                var segs = uri.AbsolutePath.Trim('/').Split('/');
                if (segs.Length > 0 && !string.IsNullOrWhiteSpace(segs[0]))
                {
                    channel = segs[0];
                }
            }
            catch { }

            string platform = lower.Contains("twitch.tv") ? "Twitch" : "Kick";
            result.Title = !string.IsNullOrWhiteSpace(channel) ? $"{platform} • {channel}" : $"Direct {platform}";
            result.AvailableResolutions = new List<string> { "1080p60", "720p60", "480p", "360p", "160p", "Audio Seul" };
            BuildAudioTiers(result);
            return result;
        }

        // 2. Appel de yt-dlp --dump-single-json (SANS --all-subs pour répondre en ~3-5 secondes au lieu de 90s)
        string ytdlpExe = ToolLocator.GetYtDlpPath();
        string? nodePath = ToolLocator.GetNodePath();
        string jsRuntimeFlag = !string.IsNullOrWhiteSpace(nodePath) ? $"--js-runtimes node:\"{nodePath}\"" : "--js-runtimes node";
        var psi = new ProcessStartInfo
        {
            FileName = ytdlpExe,
            Arguments = $"{jsRuntimeFlag} --dump-single-json --no-playlist --skip-download \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            // CRITIQUE : Lire stdout ET stderr en parallèle pour éviter tout interblocage (deadlock) de buffer OS
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

            // Timeout de sécurité de 25 secondes
            var exitTask = proc.WaitForExitAsync(cancellationToken);
            if (await Task.WhenAny(exitTask, Task.Delay(25000, cancellationToken)) == exitTask)
            {
                string json = await stdoutTask;
                string stderr = await stderrTask;

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("title", out var titleProp))
                        result.Title = titleProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("thumbnail", out var thumbProp))
                        result.ThumbnailUrl = thumbProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out var dur))
                        result.DurationSeconds = dur;

                    if (root.TryGetProperty("is_live", out var liveProp) && liveProp.ValueKind == JsonValueKind.True)
                    {
                        result.IsLive = true;
                        string channel = string.Empty;
                        if (root.TryGetProperty("uploader", out var upProp) && !string.IsNullOrWhiteSpace(upProp.GetString()))
                            channel = upProp.GetString()!;
                        else if (root.TryGetProperty("channel", out var chProp) && !string.IsNullOrWhiteSpace(chProp.GetString()))
                            channel = chProp.GetString()!;

                        if (!string.IsNullOrWhiteSpace(channel) && !result.Title.Contains(channel, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Title = $"{channel} • {result.Title}";
                        }
                    }

                    if (root.TryGetProperty("extractor", out var extProp))
                        result.Extractor = extProp.GetString() ?? string.Empty;

                    // 2.1 Extraction des sous-titres
                    var subtitlesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Aucun" };

                    // Sous-titres manuels créés par l'auteur (qualité garantie)
                    if (root.TryGetProperty("subtitles", out var subsProp) && subsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var sub in subsProp.EnumerateObject())
                        {
                            string langName = FormatFriendlyLanguage(sub.Name);
                            subtitlesSet.Add($"{langName} (intégré)");
                            subtitlesSet.Add($"{langName} (.srt séparé)");
                        }
                    }

                    // Sous-titres automatiques majeurs (uniquement si l'auteur n'a pas déjà fourni de vrais sous-titres officiels)
                    if (root.TryGetProperty("automatic_captions", out var autoSubsProp) && autoSubsProp.ValueKind == JsonValueKind.Object)
                    {
                        bool hasAuthorSubtitles = subtitlesSet.Count > 1;
                        var priorityLangs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fr", "en", "es" };

                        foreach (var sub in autoSubsProp.EnumerateObject())
                        {
                            string code = sub.Name.Split('-')[0].ToLowerInvariant();
                            // Si l'auteur a déjà fourni des sous-titres, on évite les traductions automatiques Google Translate qui déclenchent l'erreur 429
                            if (!hasAuthorSubtitles && priorityLangs.Contains(code))
                            {
                                string langName = FormatFriendlyLanguage(sub.Name);
                                subtitlesSet.Add($"{langName} (auto)");
                                subtitlesSet.Add($"{langName} (auto .srt)");
                            }
                        }
                    }

                    // 2.2 Extraction des pistes audio multiples et résolutions
                    var audioLanguagesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Originale" };
                    var resolutionsMap = new Dictionary<int, string>();
                    var videoCodecs = new HashSet<string>();
                    int maxAudioBitrate = 0;

                    if (root.TryGetProperty("formats", out var formatsProp) && formatsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var fmt in formatsProp.EnumerateArray())
                        {
                            // Audio bitrate
                            if (fmt.TryGetProperty("abr", out var abrProp) && abrProp.ValueKind == JsonValueKind.Number && abrProp.TryGetDouble(out var abr))
                            {
                                int abrInt = (int)Math.Round(abr);
                                if (abrInt > maxAudioBitrate) maxAudioBitrate = abrInt;
                            }

                            // Détection des pistes audio multilingues
                            string? lang = null;
                            if (fmt.TryGetProperty("language", out var langProp) && !string.IsNullOrWhiteSpace(langProp.GetString()))
                            {
                                lang = langProp.GetString();
                            }

                            string? formatNote = null;
                            if (fmt.TryGetProperty("format_note", out var noteProp) && !string.IsNullOrWhiteSpace(noteProp.GetString()))
                            {
                                formatNote = noteProp.GetString();
                            }

                            if (!string.IsNullOrWhiteSpace(lang) && lang != "und" && lang != "none")
                            {
                                string friendly = FormatFriendlyLanguage(lang);
                                if (!string.IsNullOrWhiteSpace(formatNote) && formatNote.Contains("dubbed", StringComparison.OrdinalIgnoreCase))
                                {
                                    audioLanguagesSet.Add($"{friendly} (doublé)");
                                }
                                else if (!string.IsNullOrWhiteSpace(formatNote) && formatNote.Contains("original", StringComparison.OrdinalIgnoreCase))
                                {
                                    audioLanguagesSet.Add($"{friendly} (original)");
                                }
                                else
                                {
                                    audioLanguagesSet.Add(friendly);
                                }
                            }
                            else if (!string.IsNullOrWhiteSpace(formatNote))
                            {
                                if (formatNote.Contains("dubbed", StringComparison.OrdinalIgnoreCase) ||
                                    formatNote.Contains("original", StringComparison.OrdinalIgnoreCase))
                                {
                                    audioLanguagesSet.Add(formatNote);
                                }
                            }

                            // Source audio codec
                            if (fmt.TryGetProperty("acodec", out var acodecProp))
                            {
                                string? acodec = acodecProp.GetString();
                                if (!string.IsNullOrEmpty(acodec) && acodec != "none")
                                {
                                    result.SourceAudioCodec = acodec;
                                }
                            }

                            // Résolution vidéo (hauteur en pixels)
                            if (fmt.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number && hProp.TryGetInt32(out var h) && h > 0)
                            {
                                if (!resolutionsMap.ContainsKey(h))
                                {
                                    string label = h switch
                                    {
                                        >= 4320 => $"{h}p (8K)",
                                        >= 2160 => $"{h}p (4K)",
                                        1440 => "1440p (2K)",
                                        1080 => "1080p (Full HD)",
                                        720 => "720p (HD)",
                                        _ => $"{h}p"
                                    };
                                    resolutionsMap[h] = label;
                                }
                            }

                            // Video codec
                            if (fmt.TryGetProperty("vcodec", out var vcodecProp))
                            {
                                string? vcodec = vcodecProp.GetString();
                                if (!string.IsNullOrEmpty(vcodec) && vcodec != "none")
                                {
                                    string simpleCodec = vcodec.Split('.')[0];
                                    videoCodecs.Add(simpleCodec);
                                }
                            }
                        }

                        result.MaxAvailableAudioBitrateKbps = maxAudioBitrate > 0 ? maxAudioBitrate : 128;
                        result.AvailableResolutions = resolutionsMap.OrderByDescending(kv => kv.Key).Select(kv => kv.Value).ToList();
                        result.AvailableVideoCodecs = new List<string>(videoCodecs);
                        result.AvailableAudioLanguages = audioLanguagesSet.ToList();
                        result.AvailableSubtitles = subtitlesSet.ToList();
                    }
                }
            }
            else
            {
                try { proc.Kill(true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Log(LocalizationService.Instance.Format("LogUrlInspectWarning", ex.Message));
            result.MaxAvailableAudioBitrateKbps = 128;
        }

        // Si yt-dlp n'a pas pu extraire le titre (site non supporté nativement), récupérer le titre HTML de la page
        if (string.IsNullOrWhiteSpace(result.Title))
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                string html = await client.GetStringAsync(url, cancellationToken);
                var m = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                {
                    string t = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                    if (!string.IsNullOrWhiteSpace(t) && !t.Equals("Byse Frontend", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Title = t;
                    }
                }
            }
            catch { }
        }

        if (result.AvailableResolutions.Count == 0)
        {
            result.AvailableResolutions = new List<string> { "1080p (Full HD)", "720p (HD)", "480p", "360p" };
        }

        // 3. Application stricte des Règles d'Honnêteté Audio & Tailles estimées
        BuildAudioTiers(result);
        CalculateVideoSizeEstimates(result);

        return result;
    }

    private static string FormatFriendlyLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "Inconnu";
        string clean = code.Trim().ToLowerInvariant();
        if (LanguageNames.TryGetValue(clean, out var name)) return name;

        string shortCode = clean.Split('-')[0];
        if (LanguageNames.TryGetValue(shortCode, out var nameShort)) return nameShort;

        return code;
    }

    private static void BuildAudioTiers(MediaInspectionResult result)
    {
        double durSec = result.DurationSeconds ?? 210.0;
        int maxBitrate = result.MaxAvailableAudioBitrateKbps;

        result.AudioTiers = new List<AudioQualityTier>
        {
            new()
            {
                Name = "Économique",
                Format = "MP3 — 96 kb/s",
                TargetBitrateKbps = 96,
                Audience = "Petit fichier",
                IsLossless = false,
                IsAvailable = true,
                EstimatedSizeStr = FormatEstimateBytes(96 * 1024 / 8 * durSec)
            },
            new()
            {
                Name = "Standard",
                Format = "MP3 — 128 kb/s",
                TargetBitrateKbps = 128,
                Audience = "Usage courant",
                IsLossless = false,
                IsAvailable = true,
                EstimatedSizeStr = FormatEstimateBytes(128 * 1024 / 8 * durSec)
            },
            new()
            {
                Name = "Bonne qualité",
                Format = "AAC — 256 kb/s",
                TargetBitrateKbps = 256,
                Audience = "Bon compromis",
                IsLossless = false,
                IsAvailable = maxBitrate >= 192,
                UnavailableReason = maxBitrate < 192 ? $"Indisponible à la source (max {maxBitrate} kb/s)" : string.Empty,
                EstimatedSizeStr = FormatEstimateBytes(256 * 1024 / 8 * durSec)
            },
            new()
            {
                Name = "Très haute qualité",
                Format = "MP3 — 320 kb/s",
                TargetBitrateKbps = 320,
                Audience = "Haute qualité avec perte",
                IsLossless = false,
                IsAvailable = maxBitrate >= 256,
                UnavailableReason = maxBitrate < 256 ? $"Indisponible à la source (max {maxBitrate} kb/s)" : string.Empty,
                EstimatedSizeStr = FormatEstimateBytes(320 * 1024 / 8 * durSec)
            },
            new()
            {
                Name = "Sans perte",
                Format = "FLAC — qualité CD, 16 bits / 44,1 kHz",
                TargetBitrateKbps = 1411,
                Audience = "Sans perte",
                IsLossless = true,
                IsAvailable = result.SourceAudioCodec.Contains("flac", StringComparison.OrdinalIgnoreCase) ||
                              result.SourceAudioCodec.Contains("alac", StringComparison.OrdinalIgnoreCase),
                UnavailableReason = "La source n'est pas sans perte (lossy Opus/AAC)",
                EstimatedSizeStr = FormatEstimateBytes(800 * 1024 / 8 * durSec)
            },
            new()
            {
                Name = "Hi-Res",
                Format = "FLAC — 24 bits / 96 kHz ou plus, selon la source",
                TargetBitrateKbps = 4608,
                Audience = "Audiophile",
                IsLossless = true,
                IsAvailable = false,
                UnavailableReason = "Flux Hi-Res non disponible sur cette source",
                EstimatedSizeStr = FormatEstimateBytes(2000 * 1024 / 8 * durSec)
            }
        };
    }

    private static void CalculateVideoSizeEstimates(MediaInspectionResult result)
    {
        double dur = result.DurationSeconds ?? 300.0;
        result.VideoEstimatedSizes["4320p"] = FormatEstimateBytes((40000 + 192) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["2160p"] = FormatEstimateBytes((15000 + 192) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["1440p"] = FormatEstimateBytes((7000 + 160) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["1080p"] = FormatEstimateBytes((3500 + 128) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["720p"] = FormatEstimateBytes((1800 + 128) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["480p"] = FormatEstimateBytes((800 + 96) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["360p"] = FormatEstimateBytes((450 + 96) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["240p"] = FormatEstimateBytes((250 + 64) * 1024 / 8 * dur);
        result.VideoEstimatedSizes["144p"] = FormatEstimateBytes((120 + 64) * 1024 / 8 * dur);
    }

    private static string FormatEstimateBytes(double bytes)
    {
        if (bytes <= 0) return "--";
        if (bytes < 1024 * 1024) return $"≈ {(bytes / 1024):0} Ko";
        if (bytes < 1024 * 1024 * 1024) return $"≈ {(bytes / (1024 * 1024)):0} Mo";
        return $"≈ {(bytes / (1024 * 1024 * 1024)):0.1} Go";
    }
}
