using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public class DownloadService
{
    private readonly SettingsService _settingsService;
    private readonly ConcurrentDictionary<string, DownloadItem> _activeLiveItems = new();
    private readonly ConcurrentDictionary<string, Process> _activeProcesses = new();
    private readonly ConcurrentDictionary<string, bool> _pausedItems = new();
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+([\d\.]+)%\s+of\s+~?\s*([\d\.]+\s*[A-Za-z]+)(?:\s+at\s+([\d\.]+\s*[A-Za-z/]+|Unknown\s*[A-Za-z/]*|N/A))?(?:\s+ETA\s+([^\s\r\n\(\)]+))?(?:\s*\(frag\s+(\d+)/(\d+)\))?", RegexOptions.Compiled);
    private static readonly Regex DestinationRegex = new(@"\[download\]\s+Destination:\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex MergerRegex = new(@"\[Merger\]\s+Merging formats into ""?([^""\r\n]+)""?", RegexOptions.Compiled);
    private static readonly Regex ExtractAudioRegex = new(@"\[ExtractAudio\]\s+Destination:\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+(.+?)\s+has already been downloaded", RegexOptions.Compiled);
    private static readonly Regex DownloadingFormatRegex = new(@"Downloading \d+ format\(s\):\s+([^\r\n]+)", RegexOptions.Compiled);
    private static readonly Regex FfmpegDurationRegex = new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2})", RegexOptions.Compiled);
    private static readonly Regex FfmpegTimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2})", RegexOptions.Compiled);
    private static readonly Regex FfmpegSpeedRegex = new(@"speed=\s*([\d\.]+)x?", RegexOptions.Compiled);
    private static readonly Regex FfmpegSizeRegex = new(@"size=\s*([0-9a-zA-Z\.]+)", RegexOptions.Compiled);

    public IReadOnlyCollection<DownloadItem> ActiveLiveItems => _activeLiveItems.Values.ToList();

    public DownloadService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void PauseDownload(DownloadItem item)
    {
        _pausedItems[item.Id] = true;
        if (_activeProcesses.TryGetValue(item.Id, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
    }

    public void ResumeDownload(DownloadItem item)
    {
        _pausedItems.TryRemove(item.Id, out _);
    }

    public void CancelDownload(DownloadItem item)
    {
        _pausedItems.TryRemove(item.Id, out _);
        if (_activeProcesses.TryGetValue(item.Id, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }
    }

    public void CleanupIntermediateFilesOnly(DownloadItem item)
    {
        var settings = _settingsService.Settings;
        string folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) ? settings.DownloadFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string cleanTitle = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"Video_{item.Id}");
        cleanTitle = SanitizeFileName(cleanTitle);

        var foldersToCheck = new List<string> { folder };
        if (!string.IsNullOrWhiteSpace(item.PlaylistFolder) && Directory.Exists(item.PlaylistFolder))
        {
            foldersToCheck.Add(item.PlaylistFolder);
        }

        foreach (var fld in foldersToCheck)
        {
            if (!Directory.Exists(fld)) continue;
            try
            {
                var dir = new DirectoryInfo(fld);
                var matchingFiles = dir.GetFiles($"{cleanTitle}.*")
                    .Where(f => f.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                                f.Name.Contains(".part-Frag", StringComparison.OrdinalIgnoreCase) ||
                                f.Name.Contains(".temp", StringComparison.OrdinalIgnoreCase));

                foreach (var f in matchingFiles)
                {
                    try { f.Delete(); } catch { }
                }
            }
            catch { }
        }
    }

    public void DeleteAllAssociatedFiles(DownloadItem item)
    {
        CancelDownload(item);

        var settings = _settingsService.Settings;
        string folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) ? settings.DownloadFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string cleanTitle = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"Video_{item.Id}");
        cleanTitle = SanitizeFileName(cleanTitle);

        string targetFolder = Path.Combine(folder, cleanTitle);

        // 1. Supprimer le sous-dossier dédié s'il existe
        if (Directory.Exists(targetFolder) && !string.Equals(folder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Delete(targetFolder, recursive: true);
            }
            catch { }
        }

        // 2. Supprimer tous les fichiers associés dans le dossier principal (fichiers terminés, fragments, .part, .ytdl)
        if (Directory.Exists(folder))
        {
            try
            {
                var dir = new DirectoryInfo(folder);
                var titlesToCheck = new List<string> { cleanTitle };
                if (!string.IsNullOrWhiteSpace(item.Title))
                {
                    string altTitle = SanitizeFileName(item.Title);
                    if (!string.Equals(altTitle, cleanTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        titlesToCheck.Add(altTitle);
                    }
                }

                foreach (var t in titlesToCheck)
                {
                    var matchingFiles = dir.GetFiles($"{t}.*");
                    foreach (var f in matchingFiles)
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            try
                            {
                                f.Delete();
                                break;
                            }
                            catch (IOException)
                            {
                                Thread.Sleep(100);
                            }
                            catch { break; }
                        }
                    }
                }
            }
            catch { }
        }

        // 3. Supprimer explicitement OutputPath ou TempTsPath s'ils subsistent ainsi que leurs extensions .part / .ytdl
        if (!string.IsNullOrWhiteSpace(item.OutputPath))
        {
            try
            {
                if (File.Exists(item.OutputPath)) File.Delete(item.OutputPath);
                if (File.Exists(item.OutputPath + ".part")) File.Delete(item.OutputPath + ".part");
                if (File.Exists(item.OutputPath + ".ytdl")) File.Delete(item.OutputPath + ".ytdl");
            }
            catch { }
        }
        if (!string.IsNullOrWhiteSpace(item.TempTsPath) && File.Exists(item.TempTsPath))
        {
            try { File.Delete(item.TempTsPath); } catch { }
        }
    }

    public async Task ExecuteDownloadAsync(DownloadItem item, Action<DownloadItem> onProgress, CancellationToken cancellationToken)
    {
        var settings = _settingsService.Settings;

        // Détection Live
        if (item.IsLive || item.Url.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase) || item.Url.Contains("kick.com", StringComparison.OrdinalIgnoreCase))
        {
            item.IsLive = true;
            await ExecuteLiveRecordingAsync(item, onProgress, cancellationToken);
            return;
        }

        await ExecuteYtDlpDownloadAsync(item, onProgress, cancellationToken);
    }

    private async Task ExecuteYtDlpDownloadAsync(DownloadItem item, Action<DownloadItem> onProgress, CancellationToken cancellationToken)
    {
        string ytdlpExe = ToolLocator.GetYtDlpPath();
        string ffmpegExe = ToolLocator.GetFfmpegPath();
        string ffmpegDir = Path.GetDirectoryName(ffmpegExe) ?? string.Empty;

        var settings = _settingsService.Settings;
        string folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) ? settings.DownloadFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);

        bool hasSubtitles = !string.IsNullOrWhiteSpace(item.SubtitleTrack) && 
                            !item.SubtitleTrack.Equals("Aucun", StringComparison.OrdinalIgnoreCase);

        string cleanTitle = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"Video_{item.Id}");
        cleanTitle = SanitizeFileName(cleanTitle);

        // Dès que des sous-titres (vtt/srt) sont générés pour la vidéo : créer un sous-dossier dédié pour y regrouper le mkv et tous les vtt/srt
        // Pour les playlists : regrouper dans le sous-dossier de la playlist et numéroter les fichiers (001 - Titre)
        string targetFolder = folder;
        if (!string.IsNullOrWhiteSpace(item.PlaylistFolder))
        {
            targetFolder = item.PlaylistFolder;
            Directory.CreateDirectory(targetFolder);
        }
        else if (hasSubtitles)
        {
            targetFolder = Path.Combine(folder, cleanTitle);
            Directory.CreateDirectory(targetFolder);
        }

        string fileName = cleanTitle;
        if (item.PlaylistIndex > 0)
        {
            string playlistName = !string.IsNullOrWhiteSpace(item.PlaylistFolder) 
                ? Path.GetFileName(item.PlaylistFolder) 
                : "Playlist";
            string namingTpl = !string.IsNullOrWhiteSpace(item.NamingTemplate) 
                ? item.NamingTemplate 
                : settings.PlaylistNamingTemplate;
            fileName = FormatPlaylistFileName(namingTpl, item.PlaylistIndex, cleanTitle, item.Id, playlistName);
        }

        string template = Path.Combine(targetFolder, $"{fileName}.%(ext)s");

        string formatArgs = BuildYtDlpFormatArgs(item, settings.SpeedLimit);

        string? nodePath = ToolLocator.GetNodePath();
        string jsRuntimeFlag = !string.IsNullOrWhiteSpace(nodePath) ? $"--js-runtimes node:\"{nodePath}\"" : "--js-runtimes node";

        string downloadUrl = !string.IsNullOrWhiteSpace(item.TargetUrl) ? item.TargetUrl : item.Url;
        string refererArg = !string.IsNullOrWhiteSpace(item.Referer) ? $"--referer \"{item.Referer}\"" : string.Empty;
        // Options de téléchargement des flux (HLS/DASH/Direct) :
        // 1. --hls-use-mpegts : assemble les fragments HLS dans un flux continu sans verrous de renommage Windows
        // 2. Reconnexion automatique pour ffmpeg
        string hlsFlags = "--hls-use-mpegts --downloader-args \"ffmpeg:-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5\"";
        string args = $"--newline --no-colors --progress --socket-timeout 20 --retries 10 --fragment-retries 10 --retry-sleep 2 {hlsFlags} {jsRuntimeFlag} --ffmpeg-location \"{ffmpegDir}\" {refererArg} {formatArgs} -o \"{template}\" \"{downloadUrl}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ytdlpExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        item.Status = JobStatus.Downloading;
        item.StatusDetail = LocalizationService.Instance["StatusStarting"];
        onProgress(item);

        Logger.Log(LocalizationService.Instance.Format("LogDownloadStarting", item.Title, item.Url));

        using var proc = new Process { StartInfo = psi };
        var errorBuilder = new System.Text.StringBuilder();
        double ffmpegTotalDurationSec = 0;
        double maxProgress = 0.0;

        proc.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            string errLine = e.Data.Trim();

            // 1. Détection de la durée totale ffmpeg (ex: Duration: 01:41:50.00)
            var durMatch = FfmpegDurationRegex.Match(errLine);
            if (durMatch.Success && durMatch.Groups.Count >= 4)
            {
                if (double.TryParse(durMatch.Groups[1].Value, out double h) &&
                    double.TryParse(durMatch.Groups[2].Value, out double m) &&
                    double.TryParse(durMatch.Groups[3].Value, out double sec))
                {
                    ffmpegTotalDurationSec = (h * 3600) + (m * 60) + sec;
                }
            }

            // 2. Détection de la progression continue ffmpeg (ex: time=00:15:30.25 bitrate=1200kbits/s speed=3.2x)
            var timeMatch = FfmpegTimeRegex.Match(errLine);
            if (timeMatch.Success && timeMatch.Groups.Count >= 4)
            {
                if (double.TryParse(timeMatch.Groups[1].Value, out double ch) &&
                    double.TryParse(timeMatch.Groups[2].Value, out double cm) &&
                    double.TryParse(timeMatch.Groups[3].Value, out double cs))
                {
                    double currentSec = (ch * 3600) + (cm * 60) + cs;
                    if (ffmpegTotalDurationSec > 0)
                    {
                        double pct = Math.Min(98.5, (currentSec / ffmpegTotalDurationSec) * 98.0);
                        maxProgress = Math.Max(maxProgress, pct);
                        item.Progress = maxProgress;
                        item.StatusDetail = string.Format(LocalizationService.Instance["StatusDownloadingStreamGeneric"], item.Progress);
                    }
                    else
                    {
                        item.StatusDetail = string.Format(LocalizationService.Instance["StatusDownloadingStreamGeneric"], item.Progress);
                    }

                    var speedMatch = FfmpegSpeedRegex.Match(errLine);
                    if (speedMatch.Success)
                    {
                        item.SpeedStr = $"{speedMatch.Groups[1].Value.Trim()}x";
                    }

                    var sizeMatch = FfmpegSizeRegex.Match(errLine);
                    if (sizeMatch.Success)
                    {
                        item.SizeStr = sizeMatch.Groups[1].Value.Trim();
                    }

                    onProgress(item);
                }
                return;
            }

            // Filtrer les lignes d'info/bannières de ffmpeg pour ne pas polluer les erreurs réelles
            if (errLine.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) ||
                errLine.StartsWith("size=", StringComparison.OrdinalIgnoreCase) ||
                errLine.Contains("configuration:") ||
                errLine.Contains("built with") ||
                errLine.Contains("libavutil") ||
                errLine.Contains("libavcodec") ||
                errLine.Contains("libavformat"))
            {
                return;
            }

            errorBuilder.AppendLine(errLine);
        };

        proc.Start();
        _activeProcesses[item.Id] = proc;
        proc.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        });

        string? line;
        bool isDownloadingSubtitle = false;
        int totalStreams = 1;
        int currentStreamIndex = 0;
        string lastDestFile = string.Empty;

        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
        {
            if (cancellationToken.IsCancellationRequested) break;
            line = line.Trim();

            // 0. Détection du nombre de flux combinés téléchargés par yt-dlp (ex: 397+251-6+251-25 -> 3 flux)
            var matchFmt = DownloadingFormatRegex.Match(line);
            if (matchFmt.Success)
            {
                var tokens = matchFmt.Groups[1].Value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length > 0)
                {
                    totalStreams = tokens.Length;
                }
            }

            // 1. Détection sous-titre en cours de téléchargement
            if (line.Contains("[info] Writing video subtitles") || line.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || line.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            {
                isDownloadingSubtitle = true;
                item.StatusDetail = LocalizationService.Instance["StatusFetchingSubtitles"];
                onProgress(item);
            }

            // 2. Détection chemin direct imprimé (uniquement pour un vrai fichier vidéo/audio, JAMAIS un sous-titre)
            if (Path.IsPathRooted(line) && IsMediaFile(line))
            {
                item.OutputPath = line;
            }

            // 3. Détection Merger (assemblage vidéo + audio + sous-titres)
            var matchMerger = MergerRegex.Match(line);
            if (matchMerger.Success)
            {
                string mergedPath = matchMerger.Groups[1].Value.Trim();
                if (IsMediaFile(mergedPath))
                {
                    item.OutputPath = mergedPath;
                }
                isDownloadingSubtitle = false;
                item.Status = JobStatus.Processing;
                item.StatusDetail = LocalizationService.Instance["StatusFinalizingStreams"];
                maxProgress = Math.Max(maxProgress, 99.0);
                item.Progress = 99.0;
                onProgress(item);
            }

            // 4. Détection Extraction audio
            var matchAudio = ExtractAudioRegex.Match(line);
            if (matchAudio.Success)
            {
                string audioPath = matchAudio.Groups[1].Value.Trim();
                if (IsMediaFile(audioPath))
                {
                    item.OutputPath = audioPath;
                }
            }

            // 5. Détection Fichier déjà présent
            var matchAlready = AlreadyDownloadedRegex.Match(line);
            if (matchAlready.Success)
            {
                string alreadyPath = matchAlready.Groups[1].Value.Trim();
                if (IsMediaFile(alreadyPath))
                {
                    item.OutputPath = alreadyPath;
                }
            }

            // 6. Destination standard (ignorer TOUT fichier sous-titre .vtt / .srt pour la sortie vidéo)
            var matchDest = DestinationRegex.Match(line);
            if (matchDest.Success)
            {
                string destFile = matchDest.Groups[1].Value.Trim();
                if (destFile.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase) || destFile.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                {
                    isDownloadingSubtitle = true;
                    item.StatusDetail = LocalizationService.Instance["StatusFetchingSubtitles"];
                    onProgress(item);
                }
                else
                {
                    isDownloadingSubtitle = false;
                    if (!string.Equals(lastDestFile, destFile, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(lastDestFile))
                        {
                            currentStreamIndex++;
                        }
                        lastDestFile = destFile;
                    }

                    item.Status = JobStatus.Downloading;
                    if (currentStreamIndex == 0)
                    {
                        item.StatusDetail = "Téléchargement de la vidéo...";
                    }
                    else
                    {
                        int totalAudio = Math.Max(1, totalStreams - 1);
                        int audioIdx = Math.Min(currentStreamIndex, totalAudio);
                        item.StatusDetail = $"Téléchargement audio {audioIdx}/{totalAudio}...";
                    }

                    if (IsMediaFile(destFile))
                    {
                        item.OutputPath = destFile;
                    }
                    onProgress(item);
                }
            }

            // 7. Progression unifiée monotone : ne retombe JAMAIS à 0% entre les flux audio/vidéo
            var match = ProgressRegex.Match(line);
            if (match.Success)
            {
                if (isDownloadingSubtitle)
                {
                    item.StatusDetail = "Récupération des sous-titres...";
                }
                else
                {
                    if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var streamPct))
                    {
                        double overallPct;
                        string fragInfo = string.Empty;
                        if (!string.IsNullOrWhiteSpace(match.Groups[5].Value) && !string.IsNullOrWhiteSpace(match.Groups[6].Value))
                        {
                            fragInfo = $" (frag {match.Groups[5].Value}/{match.Groups[6].Value})";
                        }

                        if (totalStreams <= 1)
                        {
                            overallPct = (streamPct / 100.0) * 98.0;
                            item.StatusDetail = $"Téléchargement en cours ({streamPct:0.0}%{fragInfo})...";
                        }
                        else
                        {
                            // La vidéo compte pour 70% de la progression globale
                            // Les pistes audio se partagent équitablement les 28% suivants (70% à 98%)
                            // Les 2% restants sont réservés à l'assemblage et finalisation
                            double audioTotalWeight = 28.0;
                            int audioCount = Math.Max(1, totalStreams - 1);
                            double audioSlice = audioTotalWeight / audioCount;

                            if (currentStreamIndex == 0)
                            {
                                overallPct = (streamPct / 100.0) * 70.0;
                                item.StatusDetail = string.Format(LocalizationService.Instance["StatusDownloadingVideoStream"], streamPct, fragInfo);
                            }
                            else
                            {
                                int audioIdx = Math.Min(currentStreamIndex - 1, audioCount - 1);
                                overallPct = 70.0 + (audioIdx * audioSlice) + ((streamPct / 100.0) * audioSlice);
                                item.StatusDetail = string.Format(LocalizationService.Instance["StatusDownloadingAudioStream"], audioIdx + 1, audioCount, streamPct);
                            }
                        }

                        maxProgress = Math.Max(maxProgress, overallPct);
                        item.Progress = Math.Min(98.5, maxProgress);
                    }

                    if (!string.IsNullOrWhiteSpace(match.Groups[2].Value))
                    {
                        item.SizeStr = match.Groups[2].Value.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(match.Groups[3].Value))
                    {
                        item.SpeedStr = match.Groups[3].Value.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(match.Groups[4].Value))
                    {
                        string eta = match.Groups[4].Value.Trim();
                        item.EtaStr = eta.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "--" : eta;
                    }
                }

                onProgress(item);
            }
        }

        try
        {
            await proc.WaitForExitAsync();
        }
        catch { }
        finally
        {
            _activeProcesses.TryRemove(item.Id, out _);
        }

        // 1. Détection de mise en pause (conserve les fichiers .part pour reprise future)
        bool isPaused = _pausedItems.TryGetValue(item.Id, out var p) && p;
        if (isPaused)
        {
            item.Status = JobStatus.Paused;
            item.StatusDetail = LocalizationService.Instance["StatusPaused"];
            item.SpeedStr = string.Empty;
            item.EtaStr = string.Empty;
            Logger.Log(LocalizationService.Instance.Format("LogDownloadPaused", item.Title));
            onProgress(item);
            return;
        }

        // 2. Détection d'annulation (nettoie tous les fichiers en cours)
        if (cancellationToken.IsCancellationRequested)
        {
            item.Status = JobStatus.Cancelled;
            item.StatusDetail = LocalizationService.Instance["StatusCancelled"];
            item.SpeedStr = string.Empty;
            item.EtaStr = string.Empty;
            CleanupIntermediateFilesOnly(item);
            onProgress(item);
            return;
        }

        // Dès que des fichiers vtt/srt sont générés : s'assurer que tous les fichiers vtt, srt et mkv sont regroupés dans le dossier
        if (hasSubtitles && Directory.Exists(targetFolder) && !string.Equals(folder, targetFolder, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var looseFiles = Directory.GetFiles(folder, $"{cleanTitle}.*")
                                          .Where(f => !File.GetAttributes(f).HasFlag(FileAttributes.Directory));
                foreach (var lf in looseFiles)
                {
                    string dest = Path.Combine(targetFolder, Path.GetFileName(lf));
                    if (!File.Exists(dest))
                    {
                        File.Move(lf, dest);
                    }
                }
            }
            catch { }
        }

        string resolvedFile = ResolveActualFilePath(item.OutputPath, targetFolder, item.CustomTitle, item.Title);
        bool mediaFileExists = !string.IsNullOrWhiteSpace(resolvedFile) && 
                               File.Exists(resolvedFile) && 
                               new FileInfo(resolvedFile).Length > 1024 &&
                               IsMediaFile(resolvedFile);

        if (proc.ExitCode == 0 && mediaFileExists)
        {
            // Vérification de l'intégrité réelle avec ffprobe (Étape 3)
            var integrityService = new MediaIntegrityService();
            var verified = await integrityService.VerifyMediaFileAsync(resolvedFile, cancellationToken);
            if (!verified.IsValid)
            {
                Logger.Log(LocalizationService.Instance.Format("LogIntegrityError", resolvedFile, verified.ErrorMessage), "ERROR");
                item.Status = JobStatus.Error;
                item.StatusDetail = LocalizationService.Instance["IntegrityCheckFailed"];
                item.ErrorMessage = $"{LocalizationService.Instance["FileCorruptedOrIncomplete"]} ({verified.ErrorMessage})";
                item.LastError = verified.ErrorMessage;
                onProgress(item);
                return;
            }

            item.Status = JobStatus.Finished;
            item.StatusDetail = string.Empty;
            item.Progress = 100.0;
            item.FinishedAt = DateTime.Now;
            item.OutputPath = resolvedFile;

            // Injection des noms et langues réelles des pistes audio dans le conteneur MKV/MP4
            FixAudioTrackMetadata(resolvedFile, ffmpegExe, item.AudioLanguage);

            // Synchronisation automatique d'un fichier .srt avec le nom exact pour le Lecteur Windows si sous-titres requis
            if (hasSubtitles)
            {
                EnsureMatchingSrtForWindowsPlayer(resolvedFile, targetFolder);
            }

            // Nettoyage des éventuels fragments .part ou .f*** orphelins
            CleanupIntermediateFiles(targetFolder, cleanTitle);
            CleanupIntermediateFiles(folder, cleanTitle);

            Logger.Log(LocalizationService.Instance.Format("LogDownloadCompleted", item.Title, resolvedFile), "SUCCESS");
        }
        else
        {
            string err = errorBuilder.ToString().Trim();

            // Si le téléchargement échoue uniquement à cause des sous-titres bloqués par YouTube (HTTP 429 Too Many Requests)
            if (hasSubtitles && err.Contains("Unable to download video subtitles", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log(LocalizationService.Instance.Format("LogSubtitlesRestricted", item.Title), "WARNING");
                item.StatusDetail = "Sous-titres restreints (429). Téléchargement vidéo...";
                item.SubtitleTrack = "Aucun";
                onProgress(item);

                await ExecuteYtDlpDownloadAsync(item, onProgress, cancellationToken);
                return;
            }

            // Si yt-dlp échoue car l'URL n'est pas supportée directement (site de streaming, embed, page SPA, etc.)
            // ou si l'ancien flux extrait a expiré (HTTP 403/Forbidden)
            bool isDirectStream = !string.IsNullOrWhiteSpace(item.TargetUrl) && !string.Equals(item.TargetUrl, item.Url, StringComparison.OrdinalIgnoreCase);
            bool tokenExpired = isDirectStream && (err.Contains("403") || err.Contains("Forbidden") || err.Contains("410") || err.Contains("Gone") || err.Contains("404") || err.Contains("Not Found"));

            if (settings.AutoSniff && ((err.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase) || 
                                        err.Contains("Falling back on generic information extractor", StringComparison.OrdinalIgnoreCase) && !isDirectStream) ||
                                       tokenExpired))
            {
                item.TargetUrl = string.Empty;
                var loc = LocalizationService.Instance;
                string browser = !string.IsNullOrWhiteSpace(settings.ExtractionBrowser) ? settings.ExtractionBrowser : "Chromium";
                Logger.Log(LocalizationService.Instance.Format("LogSniffSearching", item.Url, browser));
                item.StatusDetail = loc["SniffSearchingStream"];
                onProgress(item);

                var sniffer = new StreamSnifferService();
                
                // 1. Essai automatique en arrière-plan (headless)
                var sniffRes = await sniffer.SniffAsync(item.Url, headless: true, browser: browser, timeoutSeconds: 10, cancellationToken);

                // 2. Si non capturé, ouvrir la fenêtre visible pour interaction utilisateur (cliquer sur PLAY, résoudre captcha)
                if ((!sniffRes.Success || string.IsNullOrWhiteSpace(sniffRes.StreamUrl)) && !cancellationToken.IsCancellationRequested)
                {
                    item.StatusDetail = loc["SniffPromptPlay"];
                    Logger.Log(LocalizationService.Instance.Format("LogSniffVisibleWindow", browser, item.Title));
                    onProgress(item);

                    sniffRes = await sniffer.SniffAsync(item.Url, headless: false, browser: browser, timeoutSeconds: 300, cancellationToken);
                }

                if (sniffRes.Success && !string.IsNullOrWhiteSpace(sniffRes.StreamUrl))
                {
                    Logger.Log(LocalizationService.Instance.Format("LogSniffSuccess", sniffRes.StreamUrl), "SUCCESS");
                    item.TargetUrl = sniffRes.StreamUrl;
                    item.Referer = sniffRes.Referer;
                    if (string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(sniffRes.Title))
                    {
                        item.Title = sniffRes.Title;
                    }
                    item.StatusDetail = loc["SniffStreamCaptured"];
                    onProgress(item);

                    await ExecuteYtDlpDownloadAsync(item, onProgress, cancellationToken);
                    return;
                }
                else
                {
                    item.Status = JobStatus.Error;
                    item.ErrorMessage = !string.IsNullOrWhiteSpace(sniffRes.Error) ? sniffRes.Error : loc["SniffStreamFailed"];
                    Logger.Log(LocalizationService.Instance.Format("LogSniffFailed", item.Title, item.ErrorMessage), "ERROR");
                    onProgress(item);
                    return;
                }
            }

            var classification = ErrorClassifier.Classify(err, item.Attempts, settings.MaxRetries);
            item.Status = JobStatus.Error;
            item.ErrorMessage = classification.UserMessage;
            item.LastError = classification.UserMessage;
            item.StatusDetail = classification.Category switch
            {
                ErrorCategory.DiskFull => LocalizationService.Instance["DiskFullWarning"],
                ErrorCategory.RateLimited => LocalizationService.Instance["RateLimitWarning"],
                _ => classification.UserMessage
            };

            Logger.Log(LocalizationService.Instance.Format("LogDownloadError", item.Title, classification.Category, classification.UserMessage, proc.ExitCode), "ERROR");

            // Seuls les fragments orphelins irrécupérables sont supprimés. Les .part récupérables sont conservés pour la reprise.
            if (!classification.ShouldPreservePartFiles)
            {
                CleanupIntermediateFiles(targetFolder, cleanTitle);
                CleanupIntermediateFiles(folder, cleanTitle);
            }
        }

        onProgress(item);
    }

    private async Task ExecuteLiveRecordingAsync(DownloadItem item, Action<DownloadItem> onProgress, CancellationToken cancellationToken)
    {
        string streamlinkExe = ToolLocator.GetStreamlinkPath();
        string ytdlpExe = ToolLocator.GetYtDlpPath();
        string ffmpegExe = ToolLocator.GetFfmpegPath();
        string ffmpegDir = Path.GetDirectoryName(ffmpegExe) ?? string.Empty;

        var settings = _settingsService.Settings;
        string folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) ? settings.DownloadFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);

        string cleanTitle = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"Live_{item.Id}");
        cleanTitle = SanitizeFileName(cleanTitle);
        string tempTs = Path.Combine(folder, $"{cleanTitle}_{item.Id}.ts");
        string finalMp4 = Path.Combine(folder, $"{cleanTitle}.mp4");
        item.TempTsPath = tempTs;

        // 6.3 Mode démarrage & moteur de capture
        bool isYouTube = item.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || item.Url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
        bool recordFromStart = settings.LiveRecordMode.Contains("début", StringComparison.OrdinalIgnoreCase) && isYouTube;

        string extraPluginArgs = string.Empty;
        if (item.Url.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            extraPluginArgs = "--twitch-low-latency";
        }
        else if (item.Url.Contains("kick.com", StringComparison.OrdinalIgnoreCase))
        {
            extraPluginArgs = "--kick-low-latency";
        }

        ProcessStartInfo psi;
        if (recordFromStart || !File.Exists(streamlinkExe) || settings.LiveEngine.Contains("yt-dlp", StringComparison.OrdinalIgnoreCase))
        {
            string liveArgs = recordFromStart ? "--live-from-start" : "--no-live-from-start";
            psi = new ProcessStartInfo
            {
                FileName = ytdlpExe,
                Arguments = $"--newline --no-colors --ffmpeg-location \"{ffmpegDir}\" {liveArgs} -o \"{tempTs}\" \"{item.Url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            // Streamlink (Twitch, Kick, direct rapide)
            string streamlinkArgs = string.IsNullOrWhiteSpace(extraPluginArgs)
                ? $"--force -o \"{tempTs}\" --retry-streams 2 --retry-open 2 --stream-timeout 10 \"{item.Url}\" best"
                : $"--force -o \"{tempTs}\" --retry-streams 2 --retry-open 2 --stream-timeout 10 {extraPluginArgs} \"{item.Url}\" best";

            psi = new ProcessStartInfo
            {
                FileName = streamlinkExe,
                Arguments = streamlinkArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        // 6.5 Limite de durée configurée
        TimeSpan? maxDuration = null;
        string durationSetting = !string.IsNullOrWhiteSpace(item.LiveMaxDuration) ? item.LiveMaxDuration : settings.LiveMaxDuration;
        if (LiveDurationHelper.TryParseLiveDuration(durationSetting, out var parsedDuration, out _))
        {
            maxDuration = parsedDuration;
        }

        item.Status = JobStatus.Downloading;
        item.IsLive = true;
        item.IsStopLiveRequested = false;
        item.LiveDurationStr = "0s";
        item.SizeStr = "🔴 Connexion au flux...";
        item.SpeedStr = string.Empty;
        _activeLiveItems[item.Id] = item;
        onProgress(item);

        using var proc = new Process { StartInfo = psi };
        var errorOutput = new System.Text.StringBuilder();

        proc.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                errorOutput.AppendLine(e.Data);
                Logger.Log($"[Direct Live STDERR] {e.Data}", "WARN");
            }
        };

        proc.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Logger.Log($"[Direct Live STDOUT] {e.Data}", "DEBUG");
            }
        };

        proc.Start();
        _activeProcesses[item.Id] = proc;
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        DateTime? recordingStartTime = null;
        DateTime lastSpeedCheck = DateTime.Now;
        long lastBytes = 0;
        bool userStopped = false;
        bool diskSpaceLow = false;
        bool maxDurationReached = false;

        using var registration = cancellationToken.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        });

        // Surveillance continue du direct (métriques synchronisées avec l'écriture réelle, limite durée, sécurité espace disque < 20 Mo)
        var monitorTask = Task.Run(async () =>
        {
            while (!proc.HasExited && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(400);

                // Arrêt manuel demandé (💾 Terminer & Enregistrer)
                if (item.IsStopLiveRequested)
                {
                    userStopped = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    break;
                }

                // 6.7 Contrôle espace disque : arrêt si moins de 20 Mo d'espace libre
                try
                {
                    string root = Path.GetPathRoot(folder) ?? "C:\\";
                    var drive = new DriveInfo(root);
                    if (drive.AvailableFreeSpace < 20L * 1024 * 1024)
                    {
                        diskSpaceLow = true;
                        item.ErrorMessage = "Espace disque insuffisant (< 20 Mo restants). L'enregistrement a été arrêté et sauvegardé.";
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        break;
                    }
                }
                catch { }

                // Tant que le fichier n'existe pas ou ne contient pas encore de données (délai de négociation initial)
                if (!File.Exists(tempTs) || new FileInfo(tempTs).Length == 0)
                {
                    item.LiveDurationStr = "0s";
                    item.SizeStr = "🔴 Connexion au flux...";
                    item.SpeedStr = string.Empty;
                    onProgress(item);
                    continue;
                }

                // Premier paquet vidéo/audio détecté sur le disque : le compteur démarre exactement ici !
                if (recordingStartTime == null)
                {
                    recordingStartTime = DateTime.Now;
                    lastSpeedCheck = recordingStartTime.Value;
                }

                var elapsed = DateTime.Now - recordingStartTime.Value;

                // 6.5 Contrôle durée maximale basée sur la durée réelle enregistrée
                if (maxDuration.HasValue && elapsed >= maxDuration.Value)
                {
                    maxDurationReached = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    break;
                }

                // 6.2 Formatage métriques réelles : « 🔴 12m 30s • 380 Mo • 2,1 Mo/s »
                try
                {
                    var info = new FileInfo(tempTs);
                    long currentBytes = info.Length;
                    var now = DateTime.Now;
                    double dt = (now - lastSpeedCheck).TotalSeconds;
                    double speedBps = dt >= 0.5 ? Math.Max(0, (currentBytes - lastBytes) / dt) : 0;
                    if (dt >= 0.5)
                    {
                        lastBytes = currentBytes;
                        lastSpeedCheck = now;
                    }

                    string durStr = FormatLiveElapsed(elapsed);
                    string sizeStr = FormatBytes(currentBytes);
                    string speedStr = $"{FormatBytes((long)speedBps)}/s";

                    string limitInfo = maxDuration.HasValue ? $" • Limite : {LiveDurationHelper.FormatDuration(maxDuration.Value)}" : string.Empty;

                    item.LiveDurationStr = durStr;
                    item.SizeStr = $"🔴 {durStr} • {sizeStr} • {speedStr}{limitInfo}";
                    item.SpeedStr = speedStr;
                    item.Progress = 100.0;
                    onProgress(item);
                }
                catch { }
            }
        });

        await proc.WaitForExitAsync();
        await monitorTask;
        _activeLiveItems.TryRemove(item.Id, out _);

        // 6.4 & 6.7 Sauvegarde systématique et finalisation en MP4
        bool shouldFinalizeDirectly = userStopped || maxDurationReached || diskSpaceLow || cancellationToken.IsCancellationRequested;

        if (shouldFinalizeDirectly)
        {
            if (File.Exists(tempTs) && new FileInfo(tempTs).Length > 20 * 1024)
            {
                item.Status = JobStatus.Processing;
                item.StatusDetail = "Finalisation...";
                item.Progress = 50.0;
                onProgress(item);

                await ConvertTsToMp4Async(tempTs, finalMp4, ffmpegExe);

                var actualDuration = await GetActualMediaDurationAsync(finalMp4);
                long finalSize = File.Exists(finalMp4) ? new FileInfo(finalMp4).Length : 0;
                string durText = actualDuration.HasValue
                    ? FormatLiveElapsed(actualDuration.Value)
                    : (recordingStartTime.HasValue ? FormatLiveElapsed(DateTime.Now - recordingStartTime.Value) : string.Empty);

                item.OutputPath = finalMp4;
                item.Status = JobStatus.Finished;
                item.Progress = 100.0;
                item.FinishedAt = DateTime.Now;
                item.LiveDurationStr = durText;
                item.SizeStr = !string.IsNullOrWhiteSpace(durText) ? $"{FormatBytes(finalSize)} • {durText}" : FormatBytes(finalSize);
                item.SpeedStr = string.Empty;
                item.EtaStr = string.Empty;
            }
            else
            {
                item.Status = cancellationToken.IsCancellationRequested ? JobStatus.Cancelled : JobStatus.Error;
            }
        }
        else
        {
            // 6.6 Coupure ou interruption imprévue du direct
            string action = settings.LiveInterruptionAction;

            if (action.Contains("Garder", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(tempTs) && new FileInfo(tempTs).Length > 20 * 1024)
                {
                    item.Status = JobStatus.Processing;
                    item.StatusDetail = "Finalisation de l'extrait...";
                    item.Progress = 50.0;
                    onProgress(item);

                    await ConvertTsToMp4Async(tempTs, finalMp4, ffmpegExe);

                    var actualDuration = await GetActualMediaDurationAsync(finalMp4);
                    long finalSize = File.Exists(finalMp4) ? new FileInfo(finalMp4).Length : 0;
                    string durText = actualDuration.HasValue
                        ? FormatLiveElapsed(actualDuration.Value)
                        : (recordingStartTime.HasValue ? FormatLiveElapsed(DateTime.Now - recordingStartTime.Value) : string.Empty);

                    item.OutputPath = finalMp4;
                    item.Status = JobStatus.Finished;
                    item.Progress = 100.0;
                    item.FinishedAt = DateTime.Now;
                    item.LiveDurationStr = durText;
                    item.SizeStr = !string.IsNullOrWhiteSpace(durText) ? $"{FormatBytes(finalSize)} • {durText}" : FormatBytes(finalSize);
                    item.SpeedStr = string.Empty;
                    item.EtaStr = string.Empty;
                }
                else
                {
                    item.Status = JobStatus.Error;
                    item.ErrorMessage = "Direct terminé sans flux reçu.";
                }
            }
            else if (action.Contains("Réessayer", StringComparison.OrdinalIgnoreCase))
            {
                item.StatusDetail = "Reconnexion automatique au direct...";
                onProgress(item);
                await Task.Delay(3000, cancellationToken);
                await ExecuteLiveRecordingAsync(item, onProgress, cancellationToken);
                return;
            }
            else
            {
                // Demander à chaque fois
                if (File.Exists(tempTs) && new FileInfo(tempTs).Length > 20 * 1024)
                {
                    item.Status = JobStatus.LiveInterrupted;
                    item.StatusDetail = "⚠️ Direct interrompu";
                    item.TempTsPath = tempTs;
                }
                else
                {
                    item.Status = JobStatus.Error;
                    string errStr = errorOutput.ToString().Trim();
                    item.ErrorMessage = !string.IsNullOrWhiteSpace(errStr) ? errStr : "Direct interrompu sans données reçues.";
                    item.SizeStr = "--";
                    item.SpeedStr = string.Empty;
                }
            }
        }

        onProgress(item);
    }

    public async Task FinalizeInterruptedLiveAsync(DownloadItem item, Action<DownloadItem> onProgress)
    {
        string ffmpegExe = ToolLocator.GetFfmpegPath();
        var settings = _settingsService.Settings;
        string folder = !string.IsNullOrWhiteSpace(settings.DownloadFolder) ? settings.DownloadFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        string cleanTitle = !string.IsNullOrWhiteSpace(item.CustomTitle) ? item.CustomTitle : (!string.IsNullOrWhiteSpace(item.Title) ? item.Title : $"Live_{item.Id}");
        cleanTitle = SanitizeFileName(cleanTitle);
        string finalMp4 = Path.Combine(folder, $"{cleanTitle}.mp4");

        if (File.Exists(item.TempTsPath) && new FileInfo(item.TempTsPath).Length > 20 * 1024)
        {
            item.Status = JobStatus.Processing;
            item.StatusDetail = "Finalisation...";
            item.Progress = 50.0;
            onProgress(item);

            await ConvertTsToMp4Async(item.TempTsPath, finalMp4, ffmpegExe);

            var actualDuration = await GetActualMediaDurationAsync(finalMp4);
            long finalSize = File.Exists(finalMp4) ? new FileInfo(finalMp4).Length : 0;
            string durText = actualDuration.HasValue ? FormatLiveElapsed(actualDuration.Value) : string.Empty;

            item.OutputPath = finalMp4;
            item.Status = JobStatus.Finished;
            item.Progress = 100.0;
            item.FinishedAt = DateTime.Now;
            item.StatusDetail = "Extrait sauvegardé";
            item.LiveDurationStr = durText;
            item.SizeStr = !string.IsNullOrWhiteSpace(durText) ? $"{FormatBytes(finalSize)} • {durText}" : FormatBytes(finalSize);
            item.SpeedStr = string.Empty;
            item.EtaStr = string.Empty;
            onProgress(item);
        }
        else
        {
            item.Status = JobStatus.Error;
            item.ErrorMessage = "Le flux temporaire est vide ou introuvable.";
            onProgress(item);
        }
    }

    public static async Task<TimeSpan?> GetActualMediaDurationAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        string ffprobe = ToolLocator.GetFfprobePath();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                await proc.WaitForExitAsync();
                if (double.TryParse(output, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sec))
                {
                    return TimeSpan.FromSeconds(sec);
                }
            }
        }
        catch { }

        return null;
    }

    public void DiscardInterruptedLive(DownloadItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.TempTsPath) && File.Exists(item.TempTsPath))
            {
                File.Delete(item.TempTsPath);
            }
        }
        catch { }
    }

    public async Task StopAndFinalizeAllLivesAsync()
    {
        foreach (var live in _activeLiveItems.Values)
        {
            live.IsStopLiveRequested = true;
        }

        var start = DateTime.Now;
        while (_activeLiveItems.Count > 0 && (DateTime.Now - start).TotalSeconds < 10)
        {
            await Task.Delay(400);
        }
    }

    private static string FormatLiveElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        }
        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s";
        }
        return $"{elapsed.Seconds}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F1} Go";
        if (bytes >= 1024L * 1024L)
            return $"{(bytes / (1024.0 * 1024.0)):F1} Mo";
        if (bytes >= 1024L)
            return $"{(bytes / 1024.0):F0} Ko";
        return $"{bytes} o";
    }

    private static async Task ConvertTsToMp4Async(string inputTs, string outputMp4, string ffmpegExe)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = $"-y -err_detect ignore_err -avoid_negative_ts make_zero -fflags +genpts+discardcorrupt -i \"{inputTs}\" -c copy -movflags +faststart \"{outputMp4}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        await proc.WaitForExitAsync();

        try { File.Delete(inputTs); } catch { }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
    }

    public static string ExtractLanguageCode(string? langText)
    {
        if (string.IsNullOrWhiteSpace(langText) ||
            langText.Equals("Originale", StringComparison.OrdinalIgnoreCase) ||
            langText.Equals("Aucun", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string lower = langText.ToLowerInvariant();

        if (lower.Contains("anglais") || lower.Contains("english")) return "en";
        if (lower.Contains("français") || lower.Contains("francais") || lower.Contains("french")) return "fr";
        if (lower.Contains("espagnol") || lower.Contains("español") || lower.Contains("spanish")) return "es";
        if (lower.Contains("allemand") || lower.Contains("deutsch") || lower.Contains("german")) return "de";
        if (lower.Contains("italien") || lower.Contains("italiano") || lower.Contains("italian")) return "it";
        if (lower.Contains("portugais") || lower.Contains("português") || lower.Contains("portuguese")) return "pt";
        if (lower.Contains("japonais") || lower.Contains("japanese")) return "ja";
        if (lower.Contains("russe") || lower.Contains("russian")) return "ru";
        if (lower.Contains("chinois") || lower.Contains("chinese")) return "zh";
        if (lower.Contains("arabe") || lower.Contains("arabic")) return "ar";
        if (lower.Contains("coréen") || lower.Contains("coreen") || lower.Contains("korean")) return "ko";
        if (lower.Contains("hindi")) return "hi";
        if (lower.Contains("néerlandais") || lower.Contains("dutch")) return "nl";
        if (lower.Contains("polonais") || lower.Contains("polish")) return "pl";
        if (lower.Contains("turc") || lower.Contains("turkish")) return "tr";
        if (lower.Contains("suédois") || lower.Contains("swedish")) return "sv";
        if (lower.Contains("norvégien") || lower.Contains("norwegian")) return "no";
        if (lower.Contains("finnois") || lower.Contains("finnish")) return "fi";
        if (lower.Contains("danois") || lower.Contains("danish")) return "da";
        if (lower.Contains("grec") || lower.Contains("greek")) return "el";
        if (lower.Contains("ukrainien") || lower.Contains("ukrainian")) return "uk";
        if (lower.Contains("tchèque") || lower.Contains("czech")) return "cs";
        if (lower.Contains("indonésien") || lower.Contains("indonesien") || lower.Contains("indonesian")) return "id";
        if (lower.Contains("vietnamien") || lower.Contains("vietnamese")) return "vi";
        if (lower.Contains("thaï") || lower.Contains("thai")) return "th";
        if (lower.Contains("bengali") || lower.Contains("bangla")) return "bn";
        if (lower.Contains("tamoul") || lower.Contains("tamil")) return "ta";
        if (lower.Contains("télougou") || lower.Contains("telugu")) return "te";
        if (lower.Contains("marathi")) return "mr";
        if (lower.Contains("pendjabi") || lower.Contains("punjabi")) return "pa";
        if (lower.Contains("persan") || lower.Contains("farsi") || lower.Contains("persian")) return "fa";
        if (lower.Contains("ourdou") || lower.Contains("urdu")) return "ur";
        if (lower.Contains("roumain") || lower.Contains("romanian")) return "ro";
        if (lower.Contains("hongrois") || lower.Contains("hungarian")) return "hu";

        var match = Regex.Match(lower, @"\b([a-z]{2}(?:-[a-z0-9]{2,4})?)\b");
        if (match.Success)
        {
            return match.Groups[1].Value.Split('-')[0];
        }

        return string.Empty;
    }

    public static bool IsMediaFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string fileName = Path.GetFileName(path);

        // Les fichiers intermédiaires comme .f251-5.webm, .f397.mp4 ou .part ne sont JAMAIS des fichiers finaux complets
        if (Regex.IsMatch(fileName, @"\.f\d+[^.]*\.[a-zA-Z0-9]+$", RegexOptions.IgnoreCase) ||
            fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".flv" or ".ts" or ".m4v"
                   or ".mp3" or ".m4a" or ".flac" or ".wav" or ".opus" or ".ogg" or ".aac";
    }

    public static string ResolveActualFilePath(string? currentPath, string folder, string? customTitle, string? title)
    {
        // 1. Si currentPath contient un identifiant de flux intermédiaire comme .f251-5.webm, tenter de résoudre le fichier fusionné final
        if (!string.IsNullOrWhiteSpace(currentPath) && Regex.IsMatch(currentPath, @"\.f\d+[^.]*\.[a-zA-Z0-9]+$", RegexOptions.IgnoreCase))
        {
            string cleaned = Regex.Replace(currentPath, @"\.f\d+[^.]*(\.[a-zA-Z0-9]+)$", "$1");
            if (File.Exists(cleaned) && IsMediaFile(cleaned)) return cleaned;

            string[] commonExts = { ".mkv", ".mp4", ".webm", ".mp3", ".m4a", ".flac" };
            foreach (var ext in commonExts)
            {
                string testExt = Path.ChangeExtension(cleaned, ext);
                if (File.Exists(testExt) && IsMediaFile(testExt)) return testExt;
            }
        }
        else if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath) && IsMediaFile(currentPath))
        {
            return currentPath;
        }

        // 2. Recherche dans le répertoire par titre (exclusivement vrais fichiers multimédias, ignorant vtt / srt / part / f***)
        string searchTitle = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : (title ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(searchTitle) && Directory.Exists(folder))
        {
            try
            {
                var dir = new DirectoryInfo(folder);
                var files = dir.GetFiles()
                               .Where(f => IsMediaFile(f.FullName) && f.Length > 1024)
                               .OrderByDescending(f => f.LastWriteTime)
                               .ToList();

                var matched = files.FirstOrDefault(f => f.Name.Contains(searchTitle, StringComparison.OrdinalIgnoreCase) ||
                                                       searchTitle.Contains(Path.GetFileNameWithoutExtension(f.Name), StringComparison.OrdinalIgnoreCase));
                if (matched != null) return matched.FullName;

                var newest = files.FirstOrDefault();
                if (newest != null && (DateTime.Now - newest.LastWriteTime).TotalMinutes < 5)
                {
                    return newest.FullName;
                }
            }
            catch { }
        }

        return (IsMediaFile(currentPath) && File.Exists(currentPath)) ? currentPath : string.Empty;
    }

    private static string BuildYtDlpFormatArgs(DownloadItem item, string globalSpeedLimit)
    {
        var sb = new System.Text.StringBuilder();

        // 1. Limite de débit si configurée (priorité aux paramètres de fichier s'ils existent)
        string speedLimit = !string.IsNullOrWhiteSpace(item.SpeedLimit) ? item.SpeedLimit : globalSpeedLimit;
        if (!string.IsNullOrWhiteSpace(speedLimit) && !speedLimit.Equals("Illimité", StringComparison.OrdinalIgnoreCase))
        {
            if (speedLimit.Contains("500")) sb.Append("--limit-rate 500K ");
            else if (speedLimit.Contains("1 Mo")) sb.Append("--limit-rate 1M ");
            else if (speedLimit.Contains("5 Mo")) sb.Append("--limit-rate 5M ");
            else if (speedLimit.Contains("10 Mo")) sb.Append("--limit-rate 10M ");
            else if (speedLimit.Contains("20 Mo")) sb.Append("--limit-rate 20M ");
            else if (speedLimit.Contains("50 Mo")) sb.Append("--limit-rate 50M ");
            else if (speedLimit.Contains("100 Mo")) sb.Append("--limit-rate 100M ");
        }

        string q = (item.Quality ?? string.Empty).ToLowerInvariant();
        string audioLangCode = ExtractLanguageCode(item.AudioLanguage);

        // 2. Formats audio
        if (q.Contains("mp3") || q.Contains("audio") || q.Contains("aac") || q.Contains("flac") || q.Contains("sans perte") || q.Contains("hi-res"))
        {
            // Sélecteur audio avec langue si demandée
            if (!string.IsNullOrWhiteSpace(audioLangCode))
            {
                sb.Append($"-f \"ba[language^={audioLangCode}]/ba[language*={audioLangCode}]/ba\" ");
            }
            else
            {
                sb.Append("-f \"ba[format_note*=original]/ba\" ");
            }

            if (q.Contains("320")) sb.Append("-x --audio-format mp3 --audio-quality 320K");
            else if (q.Contains("128")) sb.Append("-x --audio-format mp3 --audio-quality 128K");
            else if (q.Contains("96")) sb.Append("-x --audio-format mp3 --audio-quality 96K");
            else if (q.Contains("aac") || q.Contains("256")) sb.Append("-x --audio-format m4a");
            else if (q.Contains("flac") || q.Contains("sans perte") || q.Contains("hi-res")) sb.Append("-x --audio-format flac");
            else sb.Append("-x --audio-format mp3 --audio-quality 192K");

            return sb.ToString().Trim();
        }

        // 3. Formats vidéo par résolution dynamique
        int height = 1080;
        var matchH = Regex.Match(q, @"(\d{3,4})p?");
        if (matchH.Success && int.TryParse(matchH.Groups[1].Value, out int h))
        {
            height = h;
        }

        // 3.1 Filtre FPS si précisé
        string fpsFilter = string.Empty;
        if (!string.IsNullOrWhiteSpace(item.VideoFps) && int.TryParse(Regex.Match(item.VideoFps, @"\d+").Value, out int fpsVal))
        {
            fpsFilter = $"[fps<={fpsVal}]";
        }

        // 3.2 Filtre Codec vidéo si précisé
        string vcodecFilter = string.Empty;
        if (!string.IsNullOrWhiteSpace(item.VideoCodec) && !item.VideoCodec.Contains("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (item.VideoCodec.Contains("H.264") || item.VideoCodec.Contains("AVC")) vcodecFilter = "[vcodec^=avc]";
            else if (item.VideoCodec.Contains("VP9")) vcodecFilter = "[vcodec^=vp9]";
            else if (item.VideoCodec.Contains("AV1")) vcodecFilter = "[vcodec^=av01]";
            else if (item.VideoCodec.Contains("HEVC") || item.VideoCodec.Contains("H.265")) vcodecFilter = "[vcodec^=hev]";
        }

        // 3.3 Sélecteur de flux vidéo et audio multilingue (prise en charge multi-pistes et Tout télécharger)
        string videoSelector = $"bv*[height<={height}]{fpsFilter}{vcodecFilter}";
        bool isAllAudio = string.Equals(item.AudioLanguage, "ALL", StringComparison.OrdinalIgnoreCase);
        var audioLangList = !string.IsNullOrWhiteSpace(item.AudioLanguage) && !isAllAudio
            ? item.AudioLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(ExtractLanguageCode)
                                .Where(c => !string.IsNullOrWhiteSpace(c))
                                .Distinct()
                                .ToList()
            : new List<string>();

        if (isAllAudio || audioLangList.Count > 1)
        {
            sb.Append("--audio-multistreams ");
            if (audioLangList.Count > 0)
            {
                var selectors = audioLangList.Select(c => $"ba[language^={c}]");
                string combined = string.Join("+", selectors);
                sb.Append($"-f \"({videoSelector}+{combined})/({videoSelector}+ba)/best\" ");
            }
            else
            {
                sb.Append($"-f \"({videoSelector}+ba[format_note*=original]+ba)/({videoSelector}+ba)/best\" ");
            }
        }
        else if (audioLangList.Count == 1)
        {
            string code = audioLangList[0];
            sb.Append($"-f \"({videoSelector}+ba[language^={code}])/({videoSelector}+ba[language*={code}])/({videoSelector}+ba)/(b[height<={height}])/best\" ");
        }
        else
        {
            sb.Append($"-f \"({videoSelector}+ba[format_note*=original])/({videoSelector}+ba)/(b[height<={height}])/best\" ");
        }

        // 3.4 Conteneur vidéo de sortie (MP4, MKV, WebM)
        string container = !string.IsNullOrWhiteSpace(item.ContainerFormat) && !item.ContainerFormat.Contains("Recommandé", StringComparison.OrdinalIgnoreCase)
            ? item.ContainerFormat.ToLowerInvariant().Trim()
            : "mp4";

        sb.Append($"--merge-output-format {container} --embed-metadata ");

        // 3.5 Sous-titres multilingues intégrés ou en fichiers séparés (multi-sélection et Tout télécharger)
        if (!string.IsNullOrWhiteSpace(item.SubtitleTrack) && !item.SubtitleTrack.Equals("Aucun", StringComparison.OrdinalIgnoreCase))
        {
            bool isAllSubs = string.Equals(item.SubtitleTrack, "ALL", StringComparison.OrdinalIgnoreCase);
            bool isMkv = container.Equals("mkv", StringComparison.OrdinalIgnoreCase);

            if (isAllSubs)
            {
                // Pour éviter l'erreur HTTP 429 de YouTube sur 150+ sous-titres automatiques, on télécharge tous les sous-titres créés par l'auteur
                sb.Append("--sub-langs \"all,-live_chat\" --write-subs --sleep-subtitles 2 ");
                if (!isMkv)
                {
                    sb.Append("--convert-subs srt ");
                }
                if (item.EmbedSubtitles)
                {
                    sb.Append("--embed-subs ");
                }
            }
            else
            {
                var subCodes = item.SubtitleTrack
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(ExtractLanguageCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                if (subCodes.Count > 0)
                {
                    string subCodesStr = string.Join(",", subCodes);
                    bool hasAuto = item.SubtitleTrack.Contains("auto", StringComparison.OrdinalIgnoreCase);
                    string autoFlag = hasAuto ? "--write-auto-subs --sleep-subtitles 2 " : "--sleep-subtitles 2 ";
                    sb.Append($"--sub-langs \"{subCodesStr}\" --write-subs {autoFlag}");
                    // Pour le conteneur MKV, le format .vtt est supporté nativement et conservé tel quel sans suppression
                    if (!isMkv)
                    {
                        sb.Append("--convert-subs srt ");
                    }
                    if (item.EmbedSubtitles)
                    {
                        sb.Append("--embed-subs ");
                    }
                }
            }
        }

        return sb.ToString().Trim();
    }

    private static void EnsureMatchingSrtForWindowsPlayer(string? videoPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return;

        try
        {
            string targetSrt = Path.ChangeExtension(videoPath, ".srt");
            if (File.Exists(targetSrt)) return; // Déjà présent

            string videoDir = Path.GetDirectoryName(videoPath) ?? folder;
            string videoBase = Path.GetFileNameWithoutExtension(videoPath);

            if (Directory.Exists(videoDir))
            {
                var dir = new DirectoryInfo(videoDir);
                // 1. Recherche d'un fichier .srt existant (ex: "video.de.srt" ou "video.fr.srt")
                var matchingSrt = dir.GetFiles($"{videoBase}.*.srt")
                                     .OrderByDescending(f => f.LastWriteTime)
                                     .FirstOrDefault();

                if (matchingSrt != null && matchingSrt.Exists)
                {
                    File.Copy(matchingSrt.FullName, targetSrt, overwrite: true);
                    Logger.Log(LocalizationService.Instance.Format("LogSubtitleSynced", matchingSrt.Name, Path.GetFileName(targetSrt)));
                    return;
                }

                // 2. Fallback conversion transparente depuis un .vtt vers .srt (pour compatibilité Windows Media Player)
                var matchingVtt = dir.GetFiles($"{videoBase}.*.vtt")
                                     .OrderByDescending(f => f.LastWriteTime)
                                     .FirstOrDefault() ?? dir.GetFiles("*.vtt").FirstOrDefault();

                if (matchingVtt != null && matchingVtt.Exists)
                {
                    ConvertVttToSrt(matchingVtt.FullName, targetSrt);
                    Logger.Log(LocalizationService.Instance.Format("LogSubtitleSyncedVtt", matchingVtt.Name, Path.GetFileName(targetSrt)));
                }
            }
        }
        catch { }
    }

    private static void ConvertVttToSrt(string vttPath, string srtPath)
    {
        try
        {
            var lines = File.ReadAllLines(vttPath);
            using var sw = new StreamWriter(srtPath, false, System.Text.Encoding.UTF8);
            int cueIndex = 1;
            bool inCue = false;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("Kind:", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("Language:", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Horodatages WebVTT: 00:00:01.000 --> 00:00:04.000
                if (line.Contains("-->"))
                {
                    if (!inCue)
                    {
                        sw.WriteLine(cueIndex++);
                        inCue = true;
                    }
                    string srtTimestamp = Regex.Replace(line, @"(\d{2}:\d{2}:\d{2})\.(\d{3})", "$1,$2");
                    srtTimestamp = Regex.Replace(srtTimestamp, @"(\d{2}:\d{2})\.(\d{3})", "00:$1,$2");
                    sw.WriteLine(srtTimestamp);
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    if (inCue)
                    {
                        sw.WriteLine();
                        inCue = false;
                    }
                }
                else if (inCue)
                {
                    sw.WriteLine(line);
                }
            }
        }
        catch { }
    }

    private static void CleanupIntermediateFiles(string folder, string cleanTitle)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            var dir = new DirectoryInfo(folder);
            var filesToDelete = dir.GetFiles($"{cleanTitle}.*")
                                   .Where(f => f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                                               f.Name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                                               f.Name.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) ||
                                               Regex.IsMatch(f.Name, @"\.f\d+[^.]*\.[a-zA-Z0-9]+$", RegexOptions.IgnoreCase));

            foreach (var f in filesToDelete)
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static void FixAudioTrackMetadata(string mediaPath, string ffmpegExe, string? audioLanguage)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath) || !File.Exists(ffmpegExe))
            return;

        try
        {
            var langs = !string.IsNullOrWhiteSpace(audioLanguage) && !audioLanguage.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                ? audioLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                               .Select(ExtractLanguageCode)
                               .Where(c => !string.IsNullOrWhiteSpace(c))
                               .Distinct()
                               .ToList()
                : new List<string>();

            // Si aucune langue spécifiée ou mode ALL, sonder les flux audio existants via ffprobe
            if (langs.Count == 0)
            {
                string ffprobeExe = ToolLocator.GetFfprobePath();
                if (File.Exists(ffprobeExe))
                {
                    try
                    {
                        var probePsi = new ProcessStartInfo
                        {
                            FileName = ffprobeExe,
                            Arguments = $"-v error -select_streams a -show_entries stream=index:stream_tags=language -of csv=p=0 \"{mediaPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        };
                        using var probeProc = Process.Start(probePsi);
                        if (probeProc != null)
                        {
                            string probeOut = probeProc.StandardOutput.ReadToEnd();
                            probeProc.WaitForExit(5000);
                            var lines = probeOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            foreach (var line in lines)
                            {
                                var parts = line.Split(',');
                                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                                {
                                    string probedCode = ExtractLanguageCode(parts[1].Trim());
                                    if (string.IsNullOrWhiteSpace(probedCode)) probedCode = parts[1].Trim();
                                    langs.Add(probedCode);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (langs.Count == 0) return;

            var metaSb = new System.Text.StringBuilder();
            for (int i = 0; i < langs.Count; i++)
            {
                var (iso3, title) = GetLanguageMeta(langs[i]);
                metaSb.Append($"-metadata:s:a:{i} language={iso3} -metadata:s:a:{i} title=\"{title}\" ");
            }

            string dir = Path.GetDirectoryName(mediaPath) ?? string.Empty;
            string ext = Path.GetExtension(mediaPath);
            string tempFile = Path.Combine(dir, $"temp_fixmeta_{Guid.NewGuid():N}{ext}");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-y -loglevel error -i \"{mediaPath}\" -map 0 -c copy {metaSb.ToString().Trim()} \"{tempFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            bool exited = proc.WaitForExit(60000);
            if (exited && proc.ExitCode == 0 && File.Exists(tempFile) && new FileInfo(tempFile).Length > 1024)
            {
                bool moved = false;
                for (int retry = 0; retry < 10; retry++)
                {
                    try
                    {
                        File.Move(tempFile, mediaPath, overwrite: true);
                        moved = true;
                        Logger.Log(LocalizationService.Instance.Format("LogAudioMetadataInjected", langs.Count, Path.GetFileName(mediaPath)), "SUCCESS");
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500);
                    }
                }
                if (!moved && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            else
            {
                if (!exited) { try { proc.Kill(); } catch { } }
                if (File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
            }
        }
        catch (Exception ex)
        {
            Logger.Log(LocalizationService.Instance.Format("LogAudioMetadataWarning", ex.Message));
        }
    }

    private static (string iso3, string title) GetLanguageMeta(string langCode)
    {
        string c = langCode.ToLowerInvariant().Trim();
        return c switch
        {
            "fr" or "fre" or "fra" => ("fra", "Français"),
            "en" or "eng" => ("eng", "English"),
            "es" or "spa" => ("spa", "Español"),
            "de" or "deu" or "ger" => ("deu", "Deutsch"),
            "it" or "ita" => ("ita", "Italiano"),
            "pt" or "por" => ("por", "Português"),
            "ja" or "jpn" => ("jpn", "日本語"),
            "ru" or "rus" => ("rus", "Русский"),
            "ko" or "kor" => ("kor", "한국어"),
            "ar" or "ara" => ("ara", "العربية"),
            "zh" or "zho" or "chi" => ("zho", "中文"),
            "hi" or "hin" => ("hin", "हिन्दी"),
            "tr" or "tur" => ("tur", "Türkçe"),
            "pl" or "pol" => ("pol", "Polski"),
            "vi" or "vie" => ("vie", "Tiếng Việt"),
            "nl" or "nld" or "dut" => ("nld", "Nederlands"),
            "sv" or "swe" => ("swe", "Svenska"),
            "da" or "dan" => ("dan", "Dansk"),
            "fi" or "fin" => ("fin", "Suomi"),
            "no" or "nor" => ("nor", "Norsk"),
            "cs" or "ces" or "cze" => ("ces", "Čeština"),
            "el" or "ell" or "gre" => ("ell", "Ελληνικά"),
            "id" or "ind" => ("ind", "Bahasa Indonesia"),
            "bn" or "ben" => ("ben", "বাংলা"),
            "ta" or "tam" => ("tam", "தமிழ்"),
            "te" or "tel" => ("tel", "తెలుగు"),
            "mr" or "mar" => ("mar", "मराठी"),
            "pa" or "pan" => ("pan", "ਪੰਜਾਬੀ"),
            "fa" or "fas" or "per" => ("fas", "فارسی"),
            "ur" or "urd" => ("urd", "اردو"),
            "uz" or "uzb" => ("uzb", "Oʻzbekcha"),
            "ml" or "mal" => ("mal", "മലയാളം"),
            "uk" or "ukr" => ("ukr", "Українська"),
            "ro" or "ron" or "rum" => ("ron", "Română"),
            "hu" or "hun" => ("hun", "Magyar"),
            "th" or "tha" => ("tha", "ไทย"),
            _ => (c.Length == 3 ? c : (c.Length == 2 ? c + "0" : "und"), char.ToUpper(c[0]) + c.Substring(1))
        };
    }

    public static string FormatPlaylistFileName(string? template, int index, string cleanTitle, string id, string playlistName)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "{index} - {title}";
        }

        string indexStr = index.ToString("D3");
        string result = template
            .Replace("{index}", indexStr, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", cleanTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{titre}", cleanTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", id, StringComparison.OrdinalIgnoreCase)
            .Replace("{playlist}", playlistName, StringComparison.OrdinalIgnoreCase);

        return SanitizeFileName(result);
    }
}
