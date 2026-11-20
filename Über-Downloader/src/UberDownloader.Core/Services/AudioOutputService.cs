using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public sealed class AudioOutputService
{
    private static readonly int[] Mp3SampleRates = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 };
    private static readonly int[] AacSampleRates = { 7350, 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000 };
    private static readonly int[] OpusSampleRates = { 8000, 12000, 16000, 24000, 48000 };

    public async Task<string> ApplyRequestedOutputAsync(
        DownloadItem item,
        string inputPath,
        string ffmpegPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        // DownloadService appelle ce service après yt-dlp ; MainViewModel affichera ensuite les replis ajoutés au modèle.
        item.OutputFallbacks.Clear();
        var localization = LocalizationService.Instance;
        AudioInfo? source = await ProbeAsync(inputPath, cancellationToken);
        if (source == null)
        {
            AddFallback(item, localization["AudioFallbackCannotVerify"]);
        }
        AudioOutputOptions options = ResolveOptions(item, settings, source, localization);

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            AddFallback(item, localization.Format("AudioFallbackConversionFailed", "FFmpeg introuvable"));
            return inputPath;
        }

        if (options.Codec == AudioCodec.Unknown)
        {
            AddFallback(item, localization.Format("AudioFallbackConversionFailed", "codec audio inconnu"));
            return inputPath;
        }

        string extension = GetExtension(options.Codec);
        string directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        string outputPath = Path.ChangeExtension(inputPath, extension);
        string tempPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(inputPath)}.processing-{Guid.NewGuid():N}{extension}");
        string finalPath = inputPath;
        AudioInfo? finalInfo = null;

        try
        {
            ResolveEncoderSettings(item, options, source, localization, out int? outputChannels, out int? outputSampleRate);

            var result = await RunFfmpegAsync(
                ffmpegPath,
                inputPath,
                tempPath,
                options,
                outputChannels,
                outputSampleRate,
                cancellationToken);

            if (!result.Success)
            {
                AddFallback(item, localization.Format("AudioFallbackConversionFailed",
                    string.IsNullOrWhiteSpace(result.Error) ? "échec de FFmpeg" : result.Error));
                Logger.Log($"Conversion audio impossible pour {Path.GetFileName(inputPath)} : {result.Error}", "WARNING");
                return inputPath;
            }

            finalInfo = await ProbeAsync(tempPath, cancellationToken);
            if (finalInfo == null || string.IsNullOrWhiteSpace(finalInfo.Codec))
            {
                AddFallback(item, localization["AudioFallbackCannotVerify"]);
                Logger.Log($"La sortie audio temporaire n'a pas pu être vérifiée : {tempPath}", "WARNING");
                return inputPath;
            }

            bool samePath = PathsEqual(inputPath, outputPath);
            if (!samePath && File.Exists(outputPath))
            {
                AddFallback(item, localization.Format(
                    "AudioFallbackConversionFailed",
                    "un fichier de sortie portant ce nom existe déjà"));
                Logger.Log($"Le fichier audio de sortie existe déjà, conservation de la source : {outputPath}", "WARNING");
                return inputPath;
            }

            File.Move(tempPath, outputPath, overwrite: samePath);
            finalPath = outputPath;
            item.OutputPath = finalPath;

            if (!samePath)
            {
                try
                {
                    File.Delete(inputPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logger.Log($"Le fichier audio intermédiaire {inputPath} n'a pas pu être supprimé : {ex.Message}", "WARNING");
                }
            }

            VerifyOutput(item, options, source, finalInfo, localization);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Win32Exception)
        {
            AddFallback(item, localization.Format("AudioFallbackConversionFailed", ex.Message));
            Logger.Log($"La sortie audio n'a pas pu être finalisée pour {Path.GetFileName(inputPath)} : {ex.Message}", "WARNING");
            if (!File.Exists(finalPath))
            {
                finalPath = inputPath;
            }
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
        }

        return finalPath;
    }

    public static double EstimateOutputSizeBytes(
        DownloadItem item,
        AppSettings settings,
        double durationSeconds,
        string sourceCodec,
        int? sourceSampleRate,
        int? sourceChannels)
    {
        if (durationSeconds <= 0)
        {
            return 0;
        }

        // Pour FLAC/ALAC, la taille est estimée depuis le débit PCM puis pondérée par une compression moyenne.
        var source = new AudioInfo(sourceCodec, sourceSampleRate, sourceChannels, null, null, string.Empty);
        AudioOutputOptions options = ResolveOptions(item, settings, source, LocalizationService.Instance);
        if (options.Codec is not (AudioCodec.Flac or AudioCodec.Alac))
        {
            int bitrate = options.BitrateKbps ?? 128;
            return bitrate * 1000d / 8d * durationSeconds * 1.03d;
        }

        int sampleRate = options.SampleRate ?? sourceSampleRate ?? 44100;
        int[]? supportedRates = GetSupportedSampleRates(options.Codec);
        if (supportedRates != null && !supportedRates.Contains(sampleRate))
        {
            sampleRate = supportedRates
                .OrderBy(rate => Math.Abs((long)rate - sampleRate))
                .First();
        }

        int channels = options.Channels ?? sourceChannels ?? 2;
        channels = Math.Min(channels, GetMaximumChannels(options.Codec));
        if (sourceChannels is > 0)
        {
            channels = Math.Min(channels, sourceChannels.Value);
        }

        int bitDepth = options.BitDepth ?? 16;
        double compressionRatio = IsLosslessSource(sourceCodec) ? 0.55d : 0.62d;
        return sampleRate * (double)bitDepth * channels / 8d * durationSeconds * compressionRatio;
    }

    private static AudioOutputOptions ResolveOptions(
        DownloadItem item,
        AppSettings settings,
        AudioInfo? source,
        LocalizationService localization)
    {
        AudioProfile profile = GetProfile(item.Quality);
        AudioCodec? globalCodec = ParseCodec(settings.AdvancedAudioCodec, localization);
        AudioCodec? itemCodec = ParseCodec(item.AudioCodec, localization);
        AudioCodec codec = itemCodec ?? globalCodec ?? profile.Codec;

        int? itemBitrate = ParseBitrate(item.AudioBitrate, localization);
        int? globalBitrate = ParseBitrate(settings.AdvancedAudioBitrate, localization);
        int? bitrate = itemBitrate ?? globalBitrate ?? profile.BitrateKbps;
        if (bitrate is null)
        {
            bitrate = codec switch
            {
                AudioCodec.Mp3 => 192,
                AudioCodec.Aac => 256,
                AudioCodec.Opus => 160,
                _ => null
            };
        }

        int? sampleRate = ResolveIntegerOption(
            item.AudioSampleRate,
            settings.AdvancedAudioSampleRate,
            localization,
            ParseSampleRate);
        int? bitDepth = ResolveIntegerOption(
            item.AudioBitDepth,
            settings.AdvancedAudioBitDepth,
            localization,
            ParseBitDepth);
        int? channels = ResolveChannels(item, item.AudioChannels, settings.AdvancedAudioChannels, source, localization);

        return new AudioOutputOptions(
            codec,
            bitrate,
            itemBitrate.HasValue || globalBitrate.HasValue,
            sampleRate,
            bitDepth,
            channels,
            profile.IsHiRes);
    }

    private static void ResolveEncoderSettings(
        DownloadItem item,
        AudioOutputOptions options,
        AudioInfo? source,
        LocalizationService localization,
        out int? outputChannels,
        out int? outputSampleRate)
    {
        outputChannels = options.Channels;
        outputSampleRate = options.SampleRate;

        if (options.Codec is AudioCodec.Flac or AudioCodec.Alac)
        {
            if (options.BitrateIsExplicit && options.BitrateKbps.HasValue)
            {
                AddFallback(item, localization.Format(
                    "AudioFallbackBitrateNotApplicable",
                    options.BitrateKbps.Value,
                    GetCodecLabel(options.Codec)));
            }
        }
        else if (options.BitDepth.HasValue)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackDepthNotApplicable",
                options.BitDepth.Value,
                GetCodecLabel(options.Codec)));
        }

        int maxChannels = GetMaximumChannels(options.Codec);
        int? desiredChannels = outputChannels ?? source?.Channels;
        if (desiredChannels is > 0 && desiredChannels > maxChannels)
        {
            outputChannels = maxChannels;
            AddFallback(item, localization.Format(
                "AudioFallbackChannels",
                desiredChannels.Value,
                maxChannels));
        }
        else if (desiredChannels is > 0)
        {
            outputChannels = desiredChannels;
        }

        if (outputChannels is > 0 && source?.Channels is > 0 && outputChannels > source.Channels)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackChannels",
                outputChannels.Value,
                source.Channels.Value));
            outputChannels = source.Channels;
        }

        int[]? supportedRates = GetSupportedSampleRates(options.Codec);
        if (outputSampleRate is > 0 && supportedRates != null && !supportedRates.Contains(outputSampleRate.Value))
        {
            int requestedSampleRate = outputSampleRate.Value;
            outputSampleRate = supportedRates
                .OrderBy(rate => Math.Abs((long)rate - requestedSampleRate))
                .First();
        }

        if (options.IsHiResProfile &&
            (options.Codec != AudioCodec.Flac ||
             source is null ||
             source.SampleRate is null or < 96000 ||
             source.BitDepth is null or < 24))
        {
            AddFallback(item, localization["AudioFallbackHiResSource"]);
        }
    }

    private static async Task<FfmpegResult> RunFfmpegAsync(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        AudioOutputOptions options,
        int? outputChannels,
        int? outputSampleRate,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo(ffmpegPath);
        AddArguments(startInfo,
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", inputPath,
            "-map", "0:a:0",
            "-map_metadata", "0",
            "-map_chapters", "0",
            "-vn", "-sn", "-dn",
            "-c:a", GetEncoder(options.Codec));

        if (options.Codec is not (AudioCodec.Flac or AudioCodec.Alac) && options.BitrateKbps is > 0)
        {
            AddArguments(startInfo, "-b:a", $"{options.BitrateKbps.Value}k");
            if (options.Codec == AudioCodec.Opus)
            {
                AddArguments(startInfo, "-vbr", "off");
            }
        }

        if (outputSampleRate is > 0)
        {
            AddArguments(startInfo, "-ar", outputSampleRate.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (outputChannels is > 0)
        {
            AddArguments(startInfo, "-ac", outputChannels.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (options.Codec == AudioCodec.Flac && options.BitDepth is 16 or 24)
        {
            string sampleFormat = options.BitDepth == 16 ? "s16" : "s32";
            AddArguments(startInfo, "-sample_fmt", sampleFormat,
                "-bits_per_raw_sample", options.BitDepth.Value.ToString(CultureInfo.InvariantCulture));
        }
        else if (options.Codec == AudioCodec.Alac && options.BitDepth is 16 or 24)
        {
            AddArguments(startInfo, "-sample_fmt", options.BitDepth == 16 ? "s16p" : "s32p");
        }

        AddArguments(startInfo, outputPath);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string error = await errorTask;
        _ = await outputTask;

        bool succeeded = process.ExitCode == 0 &&
                         File.Exists(outputPath) &&
                         new FileInfo(outputPath).Length > 1024;
        return new FfmpegResult(succeeded, error.Trim());
    }

    private static async Task<AudioInfo?> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        string ffprobePath = ToolLocator.GetFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
        {
            Logger.Log("ffprobe est introuvable : les propriétés audio ne peuvent pas être vérifiées.", "WARNING");
            return null;
        }

        var startInfo = CreateProcessStartInfo(ffprobePath);
        AddArguments(startInfo,
            "-v", "error",
            "-select_streams", "a:0",
            "-show_entries", "stream=codec_name,bit_rate,sample_rate,channels,channel_layout,bits_per_sample,bits_per_raw_sample:format=bit_rate,duration",
            "-of", "json",
            path);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            using var cancellationRegistration = RegisterCancellation(process, cancellationToken);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string json = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                Logger.Log($"ffprobe n'a pas pu analyser {Path.GetFileName(path)} : {error.Trim()}", "WARNING");
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out JsonElement streams) ||
                streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement stream = streams[0];
            JsonElement format = document.RootElement.TryGetProperty("format", out JsonElement formatElement)
                ? formatElement
                : default;
            int? rawDepth = ReadInteger(stream, "bits_per_raw_sample");
            int? sampleDepth = ReadInteger(stream, "bits_per_sample");
            string codec = ReadString(stream, "codec_name");
            if (string.IsNullOrWhiteSpace(codec))
            {
                return null;
            }

            return new AudioInfo(
                codec,
                ReadInteger(stream, "sample_rate"),
                ReadInteger(stream, "channels"),
                rawDepth is > 0 ? rawDepth : sampleDepth is > 0 ? sampleDepth : null,
                ReadInteger(stream, "bit_rate") ??
                    ReadInteger(format, "bit_rate") ??
                    EstimateBitrate(path, ReadDouble(format, "duration")),
                ReadString(stream, "channel_layout"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            Logger.Log($"Impossible d'analyser les propriétés audio de {Path.GetFileName(path)} : {ex.Message}", "WARNING");
            return null;
        }
    }

    private static void VerifyOutput(
        DownloadItem item,
        AudioOutputOptions options,
        AudioInfo? source,
        AudioInfo? output,
        LocalizationService localization)
    {
        if (output == null)
        {
            AddFallback(item, localization["AudioFallbackCannotVerify"]);
            return;
        }

        AudioCodec outputCodec = CanonicalCodec(output.Codec);
        if (outputCodec != options.Codec)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackCodec",
                GetCodecLabel(options.Codec),
                GetCodecLabel(outputCodec)));
        }

        if (options.Codec is not (AudioCodec.Flac or AudioCodec.Alac) && options.BitrateKbps is > 0)
        {
            int? actualBitrate = output.Bitrate is > 0 ? (int)Math.Round(output.Bitrate.Value / 1000d) : null;
            int tolerance = Math.Max(12, options.BitrateKbps.Value / 5);
            if (!actualBitrate.HasValue || Math.Abs(actualBitrate.Value - options.BitrateKbps.Value) > tolerance)
            {
                AddFallback(item, localization.Format(
                    "AudioFallbackBitrate",
                    options.BitrateKbps.Value,
                    actualBitrate?.ToString(CultureInfo.InvariantCulture) ?? localization["VideoFallbackUnknown"]));
            }
        }

        int? expectedSampleRate = options.SampleRate ?? source?.SampleRate;
        if (expectedSampleRate is > 0 && output.SampleRate != expectedSampleRate)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackSampleRate",
                expectedSampleRate.Value,
                output.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? localization["VideoFallbackUnknown"]));
        }

        if (options.Codec is AudioCodec.Flac or AudioCodec.Alac && options.BitDepth is > 0 &&
            output.BitDepth != options.BitDepth)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackBitDepth",
                options.BitDepth.Value,
                output.BitDepth?.ToString(CultureInfo.InvariantCulture) ?? localization["VideoFallbackUnknown"]));
        }

        int? expectedChannels = options.Channels ?? source?.Channels;
        int maxChannels = GetMaximumChannels(options.Codec);
        if (expectedChannels is > 0 && expectedChannels > maxChannels)
        {
            expectedChannels = maxChannels;
        }

        if (expectedChannels is > 0 && output.Channels != expectedChannels)
        {
            AddFallback(item, localization.Format(
                "AudioFallbackChannels",
                expectedChannels.Value,
                output.Channels?.ToString(CultureInfo.InvariantCulture) ?? localization["VideoFallbackUnknown"]));
        }

        if (options.IsHiResProfile &&
            (options.Codec != AudioCodec.Flac ||
             output.SampleRate is null or < 96000 ||
             output.BitDepth is null or < 24))
        {
            AddFallback(item, localization["AudioFallbackHiResSource"]);
        }

        if (options.Codec == AudioCodec.Flac && source != null && !IsLosslessSource(source.Codec))
        {
            AddFallback(item, localization.Format("AudioFallbackSourceLossy", source.Codec));
        }
    }

    private static int? ResolveIntegerOption(
        string itemValue,
        string globalValue,
        LocalizationService localization,
        Func<string, int?> parse)
    {
        string itemOption = NormalizeOption(itemValue, localization);
        if (!IsAutomatic(itemOption, localization))
        {
            return IsKeepSource(itemOption, localization) ? null : parse(itemOption);
        }

        string globalOption = NormalizeOption(globalValue, localization);
        if (IsAutomatic(globalOption, localization) || IsKeepSource(globalOption, localization))
        {
            return null;
        }

        return parse(globalOption);
    }

    private static int? ResolveChannels(
        DownloadItem item,
        string itemValue,
        string globalValue,
        AudioInfo? source,
        LocalizationService localization)
    {
        string itemOption = NormalizeOption(itemValue, localization);
        if (IsAutomatic(itemOption, localization))
        {
            itemOption = NormalizeOption(globalValue, localization);
        }

        if (IsAutomatic(itemOption, localization) || IsKeepSource(itemOption, localization))
        {
            return null;
        }

        if (MatchesOption(itemOption, localization["ChannelMono"], "mono"))
        {
            return 1;
        }
        if (MatchesOption(itemOption, localization["ChannelStereo"], "stereo"))
        {
            return 2;
        }
        if (MatchesOption(itemOption, localization["ChannelMultichannel"], "multichannel") ||
            itemOption.Contains("5.1", StringComparison.OrdinalIgnoreCase) ||
            itemOption.Contains("7.1", StringComparison.OrdinalIgnoreCase))
        {
            if (source?.Channels is > 2)
            {
                return source.Channels;
            }

            AddFallback(item, localization.Format(
                "AudioFallbackChannels",
                localization["ChannelMultichannel"],
                source?.Channels?.ToString(CultureInfo.InvariantCulture) ?? localization["VideoFallbackUnknown"]));
            return source?.Channels;
        }

        return null;
    }

    private static AudioProfile GetProfile(string? quality)
    {
        string value = (quality ?? string.Empty).ToLowerInvariant();
        if (value.Contains("hi-res", StringComparison.Ordinal) ||
            value.Contains("hi res", StringComparison.Ordinal) ||
            value.Contains("hires", StringComparison.Ordinal))
        {
            return new AudioProfile(AudioCodec.Flac, null, true);
        }
        if (value.Contains("sans perte", StringComparison.Ordinal) ||
            value.Contains("lossless", StringComparison.Ordinal) ||
            value.Contains("sin pérdida", StringComparison.Ordinal) ||
            value.Contains("sin perdida", StringComparison.Ordinal))
        {
            return new AudioProfile(AudioCodec.Flac, null, false);
        }
        if (value.Contains("aac", StringComparison.Ordinal) || value.Contains("256", StringComparison.Ordinal))
        {
            return new AudioProfile(AudioCodec.Aac, 256, false);
        }
        if (value.Contains("320", StringComparison.Ordinal))
        {
            return new AudioProfile(AudioCodec.Mp3, 320, false);
        }
        if (value.Contains("96", StringComparison.Ordinal))
        {
            return new AudioProfile(AudioCodec.Mp3, 96, false);
        }

        return new AudioProfile(AudioCodec.Mp3, 128, false);
    }

    private static AudioCodec? ParseCodec(string value, LocalizationService localization)
    {
        string option = NormalizeOption(value, localization);
        if (IsAutomatic(option, localization) || IsKeepSource(option, localization))
        {
            return null;
        }

        if (option.Contains("aac", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Aac;
        if (option.Contains("mp3", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Mp3;
        if (option.Contains("opus", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Opus;
        if (option.Contains("flac", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Flac;
        if (option.Contains("alac", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Alac;
        return null;
    }

    private static int? ParseBitrate(string value, LocalizationService localization)
    {
        string option = NormalizeOption(value, localization);
        if (IsAutomatic(option, localization) || IsKeepSource(option, localization))
        {
            return null;
        }

        return ParseFirstNumber(option);
    }

    private static int? ParseSampleRate(string value)
    {
        Match decimalMatch = Regex.Match(value, @"\d+[,.]\d+");
        if (decimalMatch.Success &&
            double.TryParse(decimalMatch.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalRate))
        {
            return (int)Math.Round(decimalRate * 1000d);
        }

        int? rate = ParseFirstNumber(value);
        if (rate.HasValue && value.Contains("khz", StringComparison.OrdinalIgnoreCase))
        {
            rate *= 1000;
        }

        return rate;
    }

    private static int? ParseBitDepth(string value) => ParseFirstNumber(value);

    private static int? ParseFirstNumber(string value)
    {
        Match match = Regex.Match(value, @"\d+");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : null;
    }

    private static string NormalizeOption(string? value, LocalizationService localization) =>
        string.IsNullOrWhiteSpace(value) ? localization["OptionAutomatic"] : localization.TranslateOption(value.Trim());

    private static bool IsAutomatic(string value, LocalizationService localization) =>
        MatchesOption(value, localization["OptionAutomatic"], "auto", "automatic", "automatique", "automático", "automatico");

    private static bool IsKeepSource(string value, LocalizationService localization) =>
        MatchesOption(value, localization["OptionKeepSource"], "keep source", "source", "conserver celle de la source");

    private static bool MatchesOption(string value, string translated, params string[] aliases) =>
        string.Equals(value, translated, StringComparison.OrdinalIgnoreCase) ||
        aliases.Any(alias => string.Equals(value, alias, StringComparison.OrdinalIgnoreCase));

    private static string GetEncoder(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "aac",
        AudioCodec.Mp3 => "libmp3lame",
        AudioCodec.Opus => "libopus",
        AudioCodec.Flac => "flac",
        AudioCodec.Alac => "alac",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Codec audio non pris en charge.")
    };

    private static string GetExtension(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac or AudioCodec.Alac => ".m4a",
        AudioCodec.Mp3 => ".mp3",
        AudioCodec.Opus => ".opus",
        AudioCodec.Flac => ".flac",
        _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Codec audio non pris en charge.")
    };

    private static string GetCodecLabel(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "AAC",
        AudioCodec.Mp3 => "MP3",
        AudioCodec.Opus => "Opus",
        AudioCodec.Flac => "FLAC",
        AudioCodec.Alac => "ALAC",
        _ => "inconnu"
    };

    private static AudioCodec CanonicalCodec(string codec) => codec.ToLowerInvariant() switch
    {
        "aac" => AudioCodec.Aac,
        "mp3" => AudioCodec.Mp3,
        "opus" => AudioCodec.Opus,
        "flac" => AudioCodec.Flac,
        "alac" => AudioCodec.Alac,
        _ => AudioCodec.Unknown
    };

    private static int GetMaximumChannels(AudioCodec codec) => codec switch
    {
        AudioCodec.Mp3 => 2,
        AudioCodec.Aac or AudioCodec.Opus or AudioCodec.Flac or AudioCodec.Alac => 8,
        _ => 2
    };

    private static int[]? GetSupportedSampleRates(AudioCodec codec) => codec switch
    {
        AudioCodec.Mp3 => Mp3SampleRates,
        AudioCodec.Aac => AacSampleRates,
        AudioCodec.Opus => OpusSampleRates,
        _ => null
    };

    private static bool IsLosslessSource(string codec)
    {
        string name = codec.ToLowerInvariant();
        return name.Contains("flac", StringComparison.Ordinal) ||
               name.Contains("alac", StringComparison.Ordinal) ||
               name.StartsWith("pcm_", StringComparison.Ordinal) ||
               name.Contains("wavpack", StringComparison.Ordinal) ||
               name.Contains("ape", StringComparison.Ordinal) ||
               name.Contains("tta", StringComparison.Ordinal) ||
               name.Contains("truehd", StringComparison.Ordinal) ||
               name.Contains("mlp", StringComparison.Ordinal);
    }

    private static int? ReadInteger(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static int? EstimateBitrate(string path, double? durationSeconds)
    {
        if (durationSeconds is not > 0)
        {
            return null;
        }

        long estimatedBitsPerSecond = (long)Math.Round(new FileInfo(path).Length * 8d / durationSeconds.Value);
        return estimatedBitsPerSecond is > 0 and <= int.MaxValue
            ? (int)estimatedBitsPerSecond
            : null;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string executable) => new()
    {
        FileName = executable,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    private static void AddArguments(ProcessStartInfo process, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            process.ArgumentList.Add(argument);
        }
    }

    private static CancellationTokenRegistration RegisterCancellation(Process process, CancellationToken token) =>
        token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }
        });

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static void AddFallback(DownloadItem item, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback) &&
            !item.OutputFallbacks.Contains(fallback, StringComparer.Ordinal))
        {
            item.OutputFallbacks.Add(fallback);
            Logger.Log(fallback, "WARNING");
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Le fichier temporaire audio {path} n'a pas pu être supprimé : {ex.Message}", "WARNING");
        }
    }

    private sealed record AudioInfo(
        string Codec,
        int? SampleRate,
        int? Channels,
        int? BitDepth,
        int? Bitrate,
        string ChannelLayout);

    private sealed record AudioProfile(AudioCodec Codec, int? BitrateKbps, bool IsHiRes);

    private sealed record AudioOutputOptions(
        AudioCodec Codec,
        int? BitrateKbps,
        bool BitrateIsExplicit,
        int? SampleRate,
        int? BitDepth,
        int? Channels,
        bool IsHiResProfile);

    private sealed record FfmpegResult(bool Success, string Error);

    private enum AudioCodec
    {
        Unknown,
        Aac,
        Mp3,
        Opus,
        Flac,
        Alac
    }
}
