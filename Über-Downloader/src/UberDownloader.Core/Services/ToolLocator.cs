using System;
using System.IO;

namespace UberDownloader.Core.Services;

public static class ToolLocator
{
    private static readonly string DefaultFfmpegFolder = @"C:\Users\HP\OneDrive\Documents\bin";

    public static string GetYtDlpPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appBin = Path.Combine(baseDir, "bin", "yt-dlp.exe");
        if (File.Exists(appBin)) return appBin;

        string appRoot = Path.Combine(baseDir, "yt-dlp.exe");
        if (File.Exists(appRoot)) return appRoot;

        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "yt-dlp.exe");
        if (File.Exists(dataBin)) return dataBin;

        string pythonScripts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "Scripts", "yt-dlp.exe");
        if (File.Exists(pythonScripts)) return pythonScripts;

        return "yt-dlp";
    }

    public static string GetStreamlinkPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appBin = Path.Combine(baseDir, "bin", "streamlink.exe");
        if (File.Exists(appBin)) return appBin;

        string appRoot = Path.Combine(baseDir, "streamlink.exe");
        if (File.Exists(appRoot)) return appRoot;

        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "streamlink.exe");
        if (File.Exists(dataBin)) return dataBin;

        string pythonScripts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "Scripts", "streamlink.exe");
        if (File.Exists(pythonScripts)) return pythonScripts;

        return "streamlink";
    }

    public static string GetFfmpegPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appBin = Path.Combine(baseDir, "bin", "ffmpeg.exe");
        if (File.Exists(appBin)) return appBin;

        string appRoot = Path.Combine(baseDir, "ffmpeg.exe");
        if (File.Exists(appRoot)) return appRoot;

        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "ffmpeg.exe");
        if (File.Exists(dataBin)) return dataBin;

        string defaultPath = Path.Combine(DefaultFfmpegFolder, "ffmpeg.exe");
        if (File.Exists(defaultPath)) return defaultPath;

        string localApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffmpeg.exe");
        if (File.Exists(localApp)) return localApp;

        return "ffmpeg";
    }

    public static string GetFfprobePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appBin = Path.Combine(baseDir, "bin", "ffprobe.exe");
        if (File.Exists(appBin)) return appBin;

        string appRoot = Path.Combine(baseDir, "ffprobe.exe");
        if (File.Exists(appRoot)) return appRoot;

        string dataBin = Path.Combine(AppPaths.DataDirectory, "bin", "ffprobe.exe");
        if (File.Exists(dataBin)) return dataBin;

        string defaultPath = Path.Combine(DefaultFfmpegFolder, "ffprobe.exe");
        if (File.Exists(defaultPath)) return defaultPath;

        return "ffprobe";
    }

    public static string? GetNodePath()
    {
        string nvmNode = @"C:\nvm4w\nodejs\node.exe";
        if (File.Exists(nvmNode)) return nvmNode;

        string progFilesNode = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(progFilesNode)) return progFilesNode;

        string localAppNode = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "node", "node.exe");
        if (File.Exists(localAppNode)) return localAppNode;

        return null;
    }

    public static string GetPythonPath()
    {
        string python313 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "python.exe");
        if (File.Exists(python313)) return python313;

        return "python";
    }

    public static string GetSnifferScriptPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string direct = Path.Combine(baseDir, "Scripts", "sniff_stream.py");
        if (File.Exists(direct)) return direct;

        string sourceDir = @"C:\Users\HP\Downloads\code-tele\Über-Downloader\src\UberDownloader.Core\Scripts\sniff_stream.py";
        if (File.Exists(sourceDir)) return sourceDir;

        string fallbackRoot = @"C:\Users\HP\Downloads\code-tele\sniff_stream.py";
        if (File.Exists(fallbackRoot)) return fallbackRoot;

        return direct;
    }
}
