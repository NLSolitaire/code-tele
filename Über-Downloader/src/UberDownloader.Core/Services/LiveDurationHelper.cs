using System;
using System.Text.RegularExpressions;

namespace UberDownloader.Core.Services;

public static class LiveDurationHelper
{
    public static bool TryParseLiveDuration(string? input, out TimeSpan? duration, out string errorMessage)
    {
        duration = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("Illimité", StringComparison.OrdinalIgnoreCase))
        {
            duration = null;
            return true;
        }

        string text = input.Trim().ToLowerInvariant();
        int totalSeconds = 0;
        bool matched = false;

        // 1. Format "1h30", "1h 30m", "2h"
        var matchH = Regex.Match(text, @"^(\d+)\s*h(?:eures?)?(?:\s*(\d+)\s*m(?:in(?:utes?)?)?)?$");
        if (matchH.Success)
        {
            int h = int.Parse(matchH.Groups[1].Value);
            int m = matchH.Groups[2].Success && !string.IsNullOrEmpty(matchH.Groups[2].Value) ? int.Parse(matchH.Groups[2].Value) : 0;
            totalSeconds = (h * 3600) + (m * 60);
            matched = true;
        }

        // 2. Format "45 min", "45m", "10m 30s"
        if (!matched)
        {
            var matchM = Regex.Match(text, @"^(\d+)\s*m(?:in(?:utes?)?)?(?:\s*(\d+)\s*s(?:ec(?:ondes?)?)?)?$");
            if (matchM.Success)
            {
                int m = int.Parse(matchM.Groups[1].Value);
                int s = matchM.Groups[2].Success && !string.IsNullOrEmpty(matchM.Groups[2].Value) ? int.Parse(matchM.Groups[2].Value) : 0;
                totalSeconds = (m * 60) + s;
                matched = true;
            }
        }

        // 3. Format "90s", "90 sec"
        if (!matched)
        {
            var matchS = Regex.Match(text, @"^(\d+)\s*s(?:ec(?:ondes?)?)?$");
            if (matchS.Success)
            {
                totalSeconds = int.Parse(matchS.Groups[1].Value);
                matched = true;
            }
        }

        // 4. Nombre pur (ex: "60" interprété comme minutes)
        if (!matched)
        {
            if (int.TryParse(text, out int rawNum))
            {
                totalSeconds = rawNum * 60;
                matched = true;
            }
        }

        if (!matched)
        {
            errorMessage = "Format non reconnu. Exemples valides : 45 min, 1h30, 60, Illimité.";
            return false;
        }

        if (totalSeconds < 60)
        {
            errorMessage = "La durée minimale est de 1 min.";
            return false;
        }

        if (totalSeconds > 300 * 60)
        {
            errorMessage = "La durée maximale est de 300 min (5 heures).";
            return false;
        }

        duration = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    public static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h{ts.Minutes:D2}";
        }
        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes} min" + (ts.Seconds > 0 ? $" {ts.Seconds}s" : string.Empty);
        }
        return $"{ts.Seconds}s";
    }
}
