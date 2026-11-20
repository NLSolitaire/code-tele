using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public sealed class VideoOutputService
{
    private static readonly HashSet<string> SupportedTransfers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bt709", "smpte170m", "bt470bg", "smpte240m", "smpte2084", "arib-std-b67",
        "iec61966-2-1", "linear", "bt2020-10", "bt2020-12"
    };

    private static readonly HashSet<string> SupportedPrimaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "bt709", "bt2020", "smpte170m", "bt470bg", "smpte432", "smpte431", "film"
    };

    private static readonly HashSet<string> SupportedMatrices = new(StringComparer.OrdinalIgnoreCase)
    {
        "bt709", "bt2020nc", "bt2020c", "smpte170m", "bt470bg", "smpte240m", "gbr"
    };

    public async Task<string> ApplyRequestedOutputAsync(
        DownloadItem item,
        string inputPath,
        string ffmpegPath,
        CancellationToken cancellationToken)
    {
        // Ce traitement intervient après yt-dlp : il transforme son fichier intermédiaire
        // sans perdre l'original lorsqu'un conteneur demandé est incompatible.
        item.OutputFallbacks.Clear();
        var localization = LocalizationService.Instance;
        var sourceInfo = await ProbeAsync(inputPath, cancellationToken);
        string requestedContainer = GetRequestedContainer(item.ContainerFormat);
        string requestedRange = GetRequestedRange(item.VideoDynamicRange);
        string targetRange = requestedRange == "Dolby Vision" && sourceInfo?.DynamicRange != "Dolby Vision"
            ? "HDR10"
            : requestedRange;

        bool shouldConvertRange = (targetRange is "SDR" or "HDR10") &&
                                  sourceInfo != null &&
                                  sourceInfo.DynamicRange != "Unknown" &&
                                  sourceInfo.DynamicRange != targetRange;
        string? filter = shouldConvertRange
            ? BuildDynamicRangeFilter(sourceInfo!, targetRange)
            : null;

        // Sans métadonnées couleur fiables, ne pas inventer une plage source :
        // le fichier est conservé et l'écart réel sera signalé après analyse.
        if (shouldConvertRange && filter == null)
        {
            Logger.Log($"Conversion de plage dynamique ignorée : métadonnées source non prises en charge pour {Path.GetFileName(inputPath)}.", "WARNING");
        }

        string finalPath = inputPath;
        bool outputCreated = false;

        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            string directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
            string outputPath = Path.ChangeExtension(inputPath, "." + requestedContainer);
            string tempPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(inputPath)}.processing-{Guid.NewGuid():N}.{requestedContainer}");

            try
            {
                if (filter != null)
                {
                    var encodeResult = await RunFfmpegAsync(
                        ffmpegPath, inputPath, tempPath, requestedContainer, filter, item, sourceInfo!, targetRange, cancellationToken);
                    if (encodeResult.Success)
                    {
                        outputCreated = TryReplaceOutput(inputPath, outputPath, tempPath);
                        if (outputCreated)
                        {
                            finalPath = outputPath;
                        }
                    }
                    else
                    {
                        Logger.Log($"Conversion de plage dynamique impossible : {encodeResult.Error}", "WARNING");
                    }

                }

                if (!outputCreated)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remuxResult = await RunFfmpegAsync(
                        ffmpegPath, inputPath, tempPath, requestedContainer, null, item, sourceInfo, targetRange, cancellationToken);
                    if (remuxResult.Success)
                    {
                        outputCreated = TryReplaceOutput(inputPath, outputPath, tempPath);
                        if (outputCreated)
                        {
                            finalPath = outputPath;
                        }
                    }
                    else
                    {
                        Logger.Log($"Remux vers {requestedContainer} impossible : {remuxResult.Error}", "WARNING");
                    }
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Log($"Le fichier temporaire {tempPath} n'a pas pu être supprimé : {ex.Message}", "WARNING");
                    }
                }
            }
        }
        else
        {
            Logger.Log("FFmpeg est introuvable : le conteneur et la plage dynamique ne peuvent pas être convertis.", "WARNING");
        }

        item.OutputPath = finalPath;

        var finalInfo = await ProbeAsync(finalPath, cancellationToken);
        AddFallbackIfNeeded(item, "VideoFallbackContainer", requestedContainer.ToUpperInvariant(), GetActualContainer(finalPath, finalInfo));

        string requestedCodec = GetRequestedCodec(item.VideoCodec);
        if (requestedCodec != "Automatique")
        {
            AddFallbackIfNeeded(item, "VideoFallbackCodec", requestedCodec, GetActualCodec(finalInfo));
        }

        int requestedFps = GetRequestedFps(item.VideoFps);
        if (requestedFps > 0)
        {
            string actualFps = finalInfo?.FrameRate is > 0
                ? finalInfo.FrameRate.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : localization["VideoFallbackUnknown"];
            if (finalInfo?.FrameRate is > 0 && finalInfo.FrameRate.Value <= requestedFps + 0.1)
            {
                actualFps = string.Empty;
            }
            AddFallbackIfNeeded(item, "VideoFallbackFps", requestedFps.ToString(CultureInfo.InvariantCulture), actualFps);
        }

        if (requestedRange != "Automatique")
        {
            string actualRange = finalInfo?.DynamicRange ?? "Unknown";
            if (!string.Equals(requestedRange, actualRange, StringComparison.OrdinalIgnoreCase))
            {
                AddFallbackIfNeeded(item, "VideoFallbackRange", requestedRange, GetRangeLabel(actualRange, localization));
            }
        }

        bool requiresHvc1 = requestedCodec == "H.265/HEVC" &&
                            finalInfo?.Codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) == true &&
                            GetContainerExtension(finalPath) is "mp4" or "mov";
        if (requiresHvc1 && !string.Equals(finalInfo?.CodecTag, "hvc1", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"Le conteneur final n'a pas conservé le tag hvc1 pour {Path.GetFileName(finalPath)}.", "WARNING");
            AddFallbackIfNeeded(item, "VideoFallbackHvc1", "hvc1", finalInfo?.CodecTag ?? localization["VideoFallbackUnknown"]);
        }

        if (sourceInfo == null && (requestedRange != "Automatique" || requestedCodec != "Automatique" || requestedFps > 0))
        {
            AddFallbackIfNeeded(item, "VideoFallbackCannotVerify", localization["VideoFallbackRequestedSettings"], localization["VideoFallbackUnknown"]);
        }

        foreach (string fallback in item.OutputFallbacks)
        {
            Logger.Log(fallback, "WARNING");
        }

        return finalPath;
    }

    private static async Task<MediaInfo?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        string ffprobePath = ToolLocator.GetFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            Logger.Log("Chemin ffprobe vide : les options vidéo ne peuvent pas être vérifiées.", "WARNING");
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(path);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                Logger.Log($"ffprobe n'a pas pu analyser {Path.GetFileName(path)} : {error.Trim()}", "WARNING");
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement root = document.RootElement;
            JsonElement? videoStream = null;
            if (root.TryGetProperty("streams", out JsonElement streams))
            {
                foreach (JsonElement stream in streams.EnumerateArray())
                {
                    if (!string.Equals(ReadString(stream, "codec_type"), "video", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool isAttachedPicture = stream.TryGetProperty("disposition", out JsonElement disposition) &&
                                             disposition.TryGetProperty("attached_pic", out JsonElement attachedPic) &&
                                             attachedPic.ValueKind == JsonValueKind.Number &&
                                             attachedPic.GetInt32() != 0;
                    if (!isAttachedPicture)
                    {
                        videoStream = stream;
                        break;
                    }
                }
            }

            if (videoStream == null)
            {
                return null;
            }

            JsonElement selectedStream = videoStream.Value;
            string transfer = ReadString(selectedStream, "color_transfer");
            string primaries = ReadString(selectedStream, "color_primaries");
            string matrix = ReadString(selectedStream, "color_space");
            bool hasDolbyVision = false;
            if (selectedStream.TryGetProperty("side_data_list", out JsonElement sideData))
            {
                hasDolbyVision = sideData.EnumerateArray()
                    .Any(entry => ReadString(entry, "side_data_type").Contains("DOVI", StringComparison.OrdinalIgnoreCase));
            }

            string dynamicRange = GetDynamicRange(transfer, matrix, primaries, hasDolbyVision);
            double? frameRate = ParseFrameRate(ReadString(selectedStream, "avg_frame_rate"));
            frameRate ??= ParseFrameRate(ReadString(selectedStream, "r_frame_rate"));
            string formatName = root.TryGetProperty("format", out JsonElement format)
                ? ReadString(format, "format_name")
                : string.Empty;

            return new MediaInfo
            {
                Codec = ReadString(selectedStream, "codec_name"),
                CodecTag = ReadString(selectedStream, "codec_tag_string"),
                Transfer = transfer,
                Primaries = primaries,
                Matrix = matrix,
                DynamicRange = dynamicRange,
                FrameRate = frameRate,
                FormatName = formatName
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or JsonException or ArgumentException)
        {
            Logger.Log($"Impossible d'analyser les propriétés vidéo de {Path.GetFileName(path)} : {ex.Message}", "WARNING");
            return null;
        }
    }

    private static async Task<FfmpegResult> RunFfmpegAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        string container,
        string? filter,
        DownloadItem item,
        MediaInfo? sourceInfo,
        string targetRange,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        AddArguments(startInfo, "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath, "-map", "0", "-c", "copy");
        if (filter != null)
        {
            string encoder = GetEncoder(item.VideoCodec, targetRange);
            AddArguments(startInfo, "-filter:v:0", filter, "-c:v:0", encoder);
            AddArguments(startInfo, GetEncoderArguments(encoder));
            if (targetRange == "SDR")
            {
                AddArguments(startInfo, "-pix_fmt:v:0", "yuv420p", "-color_primaries:v:0", "bt709",
                    "-color_trc:v:0", "bt709", "-colorspace:v:0", "bt709");
            }
            else
            {
                AddArguments(startInfo, "-pix_fmt:v:0", "yuv420p10le", "-color_primaries:v:0", "bt2020",
                    "-color_trc:v:0", "smpte2084", "-colorspace:v:0", "bt2020nc");
            }
        }

        bool isHevc = filter != null
            ? GetEncoder(item.VideoCodec, targetRange) == "libx265"
            : sourceInfo?.Codec.Equals("hevc", StringComparison.OrdinalIgnoreCase) == true;
        if (isHevc && container is "mp4" or "mov")
        {
            AddArguments(startInfo, "-tag:v:0", "hvc1");
        }

        if (container is "mp4" or "mov")
        {
            AddArguments(startInfo, "-movflags", "+faststart");
        }
        AddArguments(startInfo, "-f", GetMuxer(container), outputPath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string error = await errorTask;
            _ = await outputTask;

            bool succeeded = process.ExitCode == 0 && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024;
            return new FfmpegResult(succeeded, error.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            return new FfmpegResult(false, ex.Message);
        }
    }

    private static string? BuildDynamicRangeFilter(MediaInfo source, string targetRange)
    {
        string transfer = NormalizeColorValue(source.Transfer, SupportedTransfers);
        string primaries = NormalizeColorValue(source.Primaries, SupportedPrimaries);
        string matrix = NormalizeColorValue(source.Matrix, SupportedMatrices);
        if (string.IsNullOrWhiteSpace(transfer) || string.IsNullOrWhiteSpace(primaries) || string.IsNullOrWhiteSpace(matrix))
        {
            if (source.DynamicRange != "SDR" || !source.Matrix.Equals("bt709", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            transfer = "bt709";
            primaries = "bt709";
            matrix = "bt709";
        }

        string linearize =
            $"zscale=matrixin={matrix}:transferin={transfer}:primariesin={primaries}:matrix=gbr:transfer=linear:primaries=bt709:npl=100,format=gbrpf32le";
        if (targetRange == "SDR")
        {
            string toneMap = source.DynamicRange == "SDR" ? string.Empty : ",tonemap=tonemap=mobius:desat=0";
            return linearize + toneMap +
                   ",zscale=primaries=bt709:transfer=bt709:matrix=bt709:range=limited,format=yuv420p";
        }

        return linearize +
               ",zscale=primaries=bt2020:transfer=smpte2084:matrix=bt2020nc,format=yuv420p10le";
    }

    private static string GetDynamicRange(string transfer, string matrix, string primaries, bool hasDolbyVision)
    {
        if (hasDolbyVision)
        {
            return "Dolby Vision";
        }

        if (transfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10";
        }

        if (transfer.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase))
        {
            return "HLG";
        }

        if (transfer is not "" and not "unknown" and not "unspecified")
        {
            return "Unknown";
        }

        if (matrix.Equals("bt709", StringComparison.OrdinalIgnoreCase) ||
            primaries.Equals("bt709", StringComparison.OrdinalIgnoreCase))
        {
            return "SDR";
        }

        return "Unknown";
    }

    private static string GetRequestedContainer(string? value)
    {
        string localized = LocalizationService.Instance.TranslateOption(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(localized) ||
            string.Equals(localized, LocalizationService.Instance["OptionRecommended"], StringComparison.OrdinalIgnoreCase))
        {
            return "mp4";
        }

        string normalized = localized.Trim().TrimStart('.').ToLowerInvariant();
        return normalized is "mp4" or "mkv" or "webm" or "mov" ? normalized : "mp4";
    }

    private static string GetRequestedRange(string? value)
    {
        string localized = LocalizationService.Instance.TranslateOption(value ?? string.Empty);
        return string.IsNullOrWhiteSpace(localized) ||
               string.Equals(localized, LocalizationService.Instance["OptionAutomatic"], StringComparison.OrdinalIgnoreCase)
            ? "Automatique"
            : localized;
    }

    private static string GetRequestedCodec(string? value)
    {
        string localized = LocalizationService.Instance.TranslateOption(value ?? string.Empty);
        return string.IsNullOrWhiteSpace(localized) ||
               string.Equals(localized, LocalizationService.Instance["OptionAutomatic"], StringComparison.OrdinalIgnoreCase)
            ? "Automatique"
            : localized;
    }

    private static int GetRequestedFps(string? value)
    {
        string? match = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty, @"\d+").Value;
        return int.TryParse(match, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) ? fps : 0;
    }

    private static string GetActualCodec(MediaInfo? info)
    {
        if (info == null)
        {
            return LocalizationService.Instance["VideoFallbackUnknown"];
        }

        return info.Codec.ToLowerInvariant() switch
        {
            "h264" => "H.264/AVC",
            "hevc" => "H.265/HEVC",
            "vp9" => "VP9",
            "av1" => "AV1",
            _ => info.Codec
        };
    }

    private static string GetActualContainer(string path, MediaInfo? info)
    {
        string format = info?.FormatName ?? string.Empty;
        if (format.Contains("matroska", StringComparison.OrdinalIgnoreCase))
        {
            string extension = GetContainerExtension(path);
            return extension is "mkv" or "webm" ? extension.ToUpperInvariant() : "MATROSKA";
        }
        if (format.Contains("mov", StringComparison.OrdinalIgnoreCase))
        {
            string extension = GetContainerExtension(path);
            return extension is "mp4" or "mov" ? extension.ToUpperInvariant() : "MOV/MP4";
        }

        return GetContainerExtension(path).ToUpperInvariant();
    }

    private static string GetContainerExtension(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension is "mp4" or "mkv" or "webm" or "mov" ? extension : "unknown";
    }

    private static string GetRangeLabel(string range, LocalizationService localization) =>
        string.Equals(range, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? localization["VideoFallbackUnknown"]
            : range;

    private static string GetEncoder(string codec, string targetRange)
    {
        if (targetRange == "HDR10")
        {
            return "libx265";
        }

        if (codec.Contains("H.265", StringComparison.OrdinalIgnoreCase) ||
            codec.Contains("HEVC", StringComparison.OrdinalIgnoreCase))
        {
            return "libx265";
        }
        if (codec.Contains("VP9", StringComparison.OrdinalIgnoreCase))
        {
            return "libvpx-vp9";
        }
        if (codec.Contains("AV1", StringComparison.OrdinalIgnoreCase))
        {
            return "libsvtav1";
        }

        return "libx264";
    }

    private static string[] GetEncoderArguments(string encoder) => encoder switch
    {
        "libx265" => new[] { "-preset", "fast", "-crf", "24", "-x265-params", "log-level=error" },
        "libvpx-vp9" => new[] { "-deadline", "good", "-cpu-used", "4", "-crf", "30", "-b:v", "0" },
        "libsvtav1" => new[] { "-preset", "8", "-crf", "30" },
        _ => new[] { "-preset", "veryfast", "-crf", "22" }
    };

    private static string GetMuxer(string container) => container switch
    {
        "mkv" => "matroska",
        "webm" => "webm",
        "mov" => "mov",
        _ => "mp4"
    };

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static CancellationTokenRegistration RegisterCancellation(Process process, CancellationToken cancellationToken) =>
        cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Logger.Log($"Impossible d'arrêter le processus média : {ex.Message}", "WARNING");
            }
        });

    private static string NormalizeColorValue(string value, HashSet<string> supported) =>
        supported.Contains(value) ? value : string.Empty;

    private static double? ParseFrameRate(string value)
    {
        string[] parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double numerator) &&
            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double denominator) &&
            denominator != 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double rate) ? rate : null;
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryReplaceOutput(string inputPath, string outputPath, string tempPath)
    {
        // Un fichier de même nom peut déjà exister : on refuse de l'écraser et laisse
        // le contrôle final déclarer le repli au lieu de perdre une sortie précédente.
        try
        {
            if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Move(tempPath, outputPath, overwrite: true);
                return true;
            }

            if (File.Exists(outputPath))
            {
                Logger.Log($"Le fichier de sortie existe déjà, conservation du fichier d'origine : {outputPath}", "WARNING");
                return false;
            }

            File.Move(tempPath, outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Impossible d'utiliser le fichier de sortie {outputPath} : {ex.Message}", "WARNING");
            return false;
        }

        try
        {
            File.Delete(inputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Le fichier intermédiaire {inputPath} n'a pas pu être supprimé : {ex.Message}", "WARNING");
        }
        return true;
    }

    private static void AddFallbackIfNeeded(DownloadItem item, string key, string requested, string actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return;
        }

        var localization = LocalizationService.Instance;
        string fallback = localization.Format(key, requested, actual);
        if (!item.OutputFallbacks.Contains(fallback, StringComparer.Ordinal))
        {
            item.OutputFallbacks.Add(fallback);
        }
    }

    private sealed class MediaInfo
    {
        public string Codec { get; init; } = string.Empty;
        public string CodecTag { get; init; } = string.Empty;
        public string Transfer { get; init; } = string.Empty;
        public string Primaries { get; init; } = string.Empty;
        public string Matrix { get; init; } = string.Empty;
        public string DynamicRange { get; init; } = "Unknown";
        public double? FrameRate { get; init; }
        public string FormatName { get; init; } = string.Empty;
    }

    private sealed record FfmpegResult(bool Success, string Error);
}
