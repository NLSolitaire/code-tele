using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace UberDownloader.Core.Services;

public static class DiskSpaceHelper
{
    public const long MinimumAllowedBytes = 1024L; // 1 Ko / KB

    public static bool TryParseDiskSpace(string? input, out long bytes, out string errorMessage)
    {
        bytes = 0;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = LocalizationService.Instance["DiskSpaceInvalidFormat"];
            return false;
        }

        string text = input.Trim().ToLowerInvariant().Replace(',', '.');

        // Pattern : nombre décimal optionnel + unité (ko/kb, mo/mb, go/gb, to/tb)
        var match = Regex.Match(text, @"^([0-9]+(?:\.[0-9]+)?)\s*(k[ob]|m[ob]|g[ob]|t[ob])?$");
        if (!match.Success)
        {
            errorMessage = LocalizationService.Instance["DiskSpaceInvalidFormat"];
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            errorMessage = LocalizationService.Instance["DiskSpaceInvalidFormat"];
            return false;
        }

        string unit = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value) ? match.Groups[2].Value : "mo";
        double multiplier = unit switch
        {
            "ko" or "kb" => 1024.0,
            "mo" or "mb" => 1024.0 * 1024.0,
            "go" or "gb" => 1024.0 * 1024.0 * 1024.0,
            "to" or "tb" => 1024.0 * 1024.0 * 1024.0 * 1024.0,
            _ => 1024.0 * 1024.0
        };

        double totalBytes = value * multiplier;
        if (totalBytes < MinimumAllowedBytes)
        {
            errorMessage = LocalizationService.Instance["DiskSpaceMinimumError"];
            return false;
        }

        bytes = (long)totalBytes;
        return true;
    }

    public static string FormatDiskSpace(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024 * 1024):0.##} To";
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):0.##} Go";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):0.##} Mo";
        return $"{bytes / 1024.0:0.##} Ko";
    }

    public static long GetFreeDiskSpace(string folderPath)
    {
        try
        {
            string root = Path.GetPathRoot(folderPath) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return long.MaxValue;
        }
    }
}
