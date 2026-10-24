using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UberDownloader.Core.Models;

namespace UberDownloader.Core.Services;

public class PlaylistService
{
    private static readonly Regex PlaylistUrlRegex = new(
        @"(?:[?&]list=([a-zA-Z0-9_-]+)|/(?:playlist|sets|album)/)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool IsPlaylistUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return PlaylistUrlRegex.IsMatch(url);
    }

    public async Task<PlaylistInspectionResult?> InspectPlaylistAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string ytdlpExe = ToolLocator.GetYtDlpPath();
        string? nodePath = ToolLocator.GetNodePath();
        string jsRuntimeFlag = !string.IsNullOrWhiteSpace(nodePath) ? $"--js-runtimes node:\"{nodePath}\"" : "--js-runtimes node";

        var psi = new ProcessStartInfo
        {
            FileName = ytdlpExe,
            Arguments = $"{jsRuntimeFlag} --flat-playlist -J --skip-download \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

            var exitTask = proc.WaitForExitAsync(cancellationToken);
            if (await Task.WhenAny(exitTask, Task.Delay(45000, cancellationToken)) != exitTask)
            {
                try { proc.Kill(true); } catch { }
                return null;
            }

            string json = await stdoutTask;
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new PlaylistInspectionResult
            {
                SourceUrl = url,
                PlaylistId = root.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty,
                Title = root.TryGetProperty("title", out var titleProp) ? (titleProp.GetString() ?? "Playlist") : "Playlist",
                Uploader = root.TryGetProperty("uploader", out var upProp) ? (upProp.GetString() ?? string.Empty) : string.Empty
            };

            if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
            {
                int index = 1;
                foreach (var entry in entriesProp.EnumerateArray())
                {
                    string entryId = entry.TryGetProperty("id", out var eid) ? (eid.GetString() ?? string.Empty) : string.Empty;
                    string entryTitle = entry.TryGetProperty("title", out var et) ? (et.GetString() ?? $"Vidéo {index}") : $"Vidéo {index}";
                    string entryUrl = entry.TryGetProperty("url", out var eu) ? (eu.GetString() ?? string.Empty) : string.Empty;

                    if (string.IsNullOrWhiteSpace(entryUrl) && !string.IsNullOrWhiteSpace(entryId))
                    {
                        entryUrl = $"https://www.youtube.com/watch?v={entryId}";
                    }

                    double? duration = null;
                    if (entry.TryGetProperty("duration", out var durProp) && durProp.ValueKind == JsonValueKind.Number && durProp.TryGetDouble(out var d))
                    {
                        duration = d;
                    }

                    string thumb = string.Empty;
                    if (entry.TryGetProperty("thumbnails", out var thumbsProp) && thumbsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var th in thumbsProp.EnumerateArray())
                        {
                            if (th.TryGetProperty("url", out var tu) && !string.IsNullOrWhiteSpace(tu.GetString()))
                            {
                                thumb = tu.GetString()!;
                            }
                        }
                    }

                    bool isAvailable = true;
                    string unavailableReason = string.Empty;

                    if (entryTitle.StartsWith("[Private video]", StringComparison.OrdinalIgnoreCase) ||
                        entryTitle.StartsWith("[Vidéo privée]", StringComparison.OrdinalIgnoreCase))
                    {
                        isAvailable = false;
                        unavailableReason = "Vidéo privée";
                    }
                    else if (entryTitle.StartsWith("[Deleted video]", StringComparison.OrdinalIgnoreCase) ||
                             entryTitle.StartsWith("[Vidéo supprimée]", StringComparison.OrdinalIgnoreCase))
                    {
                        isAvailable = false;
                        unavailableReason = "Vidéo supprimée";
                    }

                    result.Entries.Add(new PlaylistEntryInfo
                    {
                        Index = index,
                        Id = entryId,
                        Title = entryTitle,
                        Url = entryUrl,
                        DurationSeconds = duration,
                        ThumbnailUrl = thumb,
                        IsAvailable = isAvailable,
                        UnavailableReason = unavailableReason
                    });

                    index++;
                }
            }

            result.TotalVideos = result.Entries.Count;
            return result;
        }
        catch (Exception ex)
        {
            Logger.Log(LocalizationService.Instance.Format("LogPlaylistInspectError", ex.Message), "ERROR");
            return null;
        }
    }
}
