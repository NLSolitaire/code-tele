using System;
using System.IO;

namespace UberDownloader.Core.Services;

public static class Logger
{
    private static readonly string LogFilePath;
    private static readonly object Lock = new();

    static Logger()
    {
        LogFilePath = Path.Combine(AppPaths.DataDirectory, "uber_downloader.log");
    }

    public static string GetLogFilePath() => LogFilePath;

    public static void Log(string message, string level = "INFO")
    {
        lock (Lock)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line);
            }
            catch { }
        }
    }

    public static string ReadAllLogs()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    return File.ReadAllText(LogFilePath);
                }
            }
            catch { }
            return LocalizationService.Instance["LogNoLogs"];
        }
    }

    public static void ClearLogs()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, string.Empty);
                }
            }
            catch { }
        }
    }
}
